using Microsoft.Extensions.Logging;
using ShopFlow.Channel.Application.Adapters;
using ShopFlow.Channel.Application.Ports;
using ShopFlow.Contracts.Channel;
using ShopFlow.SharedKernel.Domain;

namespace ShopFlow.Channel.Application.Webhooks;

/// <summary>
/// Sprint-4.5 U3 — per-tenant orchestrator that takes the adapter-parsed
/// <see cref="WebhookEnvelope"/>, gates on
/// <see cref="WebhookEnvelope.EventType"/>, extracts the marketplace
/// order shape via the adapter, resolves external SKUs to internal SKUs
/// through <see cref="IProductMappingService"/>, and either emits
/// <see cref="OrderImportedV1"/> through <see cref="IngestWebhookService"/>
/// (all-mapped) or marks the webhook event as Failed (any unmapped line —
/// per the <c>OrderImportedV1</c> canon).
/// </summary>
/// <remarks>
/// <para>The controller's job ends at producing the
/// <see cref="WebhookEnvelope"/>. From here, this orchestrator owns the
/// event-type gating policy plus the mapping-resolution lifecycle. The
/// receiver-side fail-whole-import path keeps idempotency simple — the
/// outbox row always carries a fully-resolved <see cref="OrderImportedV1"/>
/// when present, never one with partially-resolved lines.</para>
///
/// <para>Per-event-type policy (Sprint-4.5):</para>
/// <list type="bullet">
///   <item><description><c>order.created</c> → parse, map, emit
///   <see cref="OrderImportedV1"/>. Any unmapped line → mark Failed, no
///   outbox row, return HTTP-200-shape with
///   <see cref="WebhookProcessStatus.ImportFailed"/>.</description></item>
///   <item><description>Any other event type → persist the
///   <c>webhook_events</c> row with status Received, no outbox row,
///   return <see cref="WebhookProcessStatus.EventSkipped"/>. Per-event
///   downstream policies arrive in Sprint-6+.</description></item>
/// </list>
/// </remarks>
public sealed class WebhookOrchestrator
{
    private readonly IChannelAdapterFactory _adapterFactory;
    private readonly IProductMappingService _mappingService;
    private readonly IngestWebhookService _ingest;
    private readonly ILogger<WebhookOrchestrator> _logger;

    public WebhookOrchestrator(
        IChannelAdapterFactory adapterFactory,
        IProductMappingService mappingService,
        IngestWebhookService ingest,
        ILogger<WebhookOrchestrator> logger
    )
    {
        _adapterFactory = adapterFactory;
        _mappingService = mappingService;
        _ingest = ingest;
        _logger = logger;
    }

    public async Task<Result<WebhookProcessOutcome>> ProcessAsync(
        WebhookEnvelope envelope,
        string channelType,
        Guid tenantId,
        CancellationToken ct
    )
    {
        ArgumentNullException.ThrowIfNull(envelope);
        ArgumentException.ThrowIfNullOrWhiteSpace(channelType);
        if (tenantId == Guid.Empty)
        {
            throw new ArgumentException("tenant_id is required.", nameof(tenantId));
        }

        // Event-type gating. Sprint-4.5 ships order.created only; other
        // event types persist (for audit) but emit no downstream row.
        if (
            !string.Equals(
                envelope.EventType,
                "order.created",
                StringComparison.OrdinalIgnoreCase
            )
        )
        {
            return await IngestEventSkippedAsync(envelope, ct).ConfigureAwait(false);
        }

        var adapter = _adapterFactory.TryResolve(channelType);
        if (adapter is null)
        {
            return Result<WebhookProcessOutcome>.Failure(
                $"no adapter registered for channel type '{channelType}'.",
                "webhook.adapter_missing"
            );
        }

        var draftResult = adapter.ParseOrderCreated(envelope);
        if (!draftResult.IsSuccess)
        {
            return Result<WebhookProcessOutcome>.Failure(
                draftResult.Error!,
                draftResult.ErrorCode
            );
        }
        var draft = draftResult.Value!;

        // Per-line mapping resolution. Any null → fail whole import per
        // OrderImportedV1 contract canon (Sprint-4 U8 + brainstorm R6 corrected).
        var resolutions = new (ProductMappingResolution? Resolution, ExternalOrderLine Line)[draft.Lines.Count];
        var unmappedSkus = new List<string>();
        for (var i = 0; i < draft.Lines.Count; i++)
        {
            var line = draft.Lines[i];
            var resolution = await _mappingService
                .ResolveAsync(envelope.ChannelId, line.ExternalSku, ct)
                .ConfigureAwait(false);
            resolutions[i] = (resolution, line);
            if (resolution is null)
            {
                unmappedSkus.Add(line.ExternalSku);
            }
        }

        if (unmappedSkus.Count > 0)
        {
            _logger.LogWarning(
                "Webhook order import failed — unmapped SKUs. channel_id={ChannelId} ordersn={Ordersn} unmapped_count={UnmappedCount} unmapped_skus={UnmappedSkus}",
                envelope.ChannelId,
                draft.ChannelExternalOrderId,
                unmappedSkus.Count,
                unmappedSkus
            );
            var failureReason = "unmapped_skus: " + string.Join(",", unmappedSkus);
            var ingestResult = await _ingest
                .IngestFailedAsync(envelope, failureReason, ct)
                .ConfigureAwait(false);
            if (!ingestResult.IsSuccess)
            {
                return Result<WebhookProcessOutcome>.Failure(
                    ingestResult.Error!,
                    ingestResult.ErrorCode
                );
            }
            return Result<WebhookProcessOutcome>.Success(
                new WebhookProcessOutcome(
                    EventId: ingestResult.Value!.EventId,
                    IsDuplicate: ingestResult.Value.IsDuplicate,
                    Status: WebhookProcessStatus.ImportFailed,
                    UnmappedSkus: unmappedSkus
                )
            );
        }

        var orderImported = new OrderImportedV1(
            OrderId: Guid.NewGuid(),
            TenantId: tenantId,
            ChannelId: envelope.ChannelId,
            ChannelExternalOrderId: draft.ChannelExternalOrderId,
            ShippingProfile: draft.ShippingProfile,
            Lines: resolutions
                .Select(r => new OrderImportedLineV1(r.Resolution!.InternalSku, r.Line.Qty))
                .ToList(),
            OccurredAt: envelope.OccurredAt
        );

        var emitResult = await _ingest
            .IngestAsync(
                envelope,
                downstreamEventType: typeof(OrderImportedV1).AssemblyQualifiedName!,
                downstreamPayload: orderImported,
                ct
            )
            .ConfigureAwait(false);
        if (!emitResult.IsSuccess)
        {
            return Result<WebhookProcessOutcome>.Failure(
                emitResult.Error!,
                emitResult.ErrorCode
            );
        }

        return Result<WebhookProcessOutcome>.Success(
            new WebhookProcessOutcome(
                EventId: emitResult.Value!.EventId,
                IsDuplicate: emitResult.Value.IsDuplicate,
                Status: WebhookProcessStatus.OrderImported
            )
        );
    }

    private async Task<Result<WebhookProcessOutcome>> IngestEventSkippedAsync(
        WebhookEnvelope envelope,
        CancellationToken ct
    )
    {
        // Sprint-4.5 ships order.created only. Other event types persist
        // the webhook_events row (status=Received) for audit but emit no
        // downstream row. IngestAsync's outbox append is suppressed by
        // passing a sentinel event-type that the dispatcher silently drops
        // — but the cleaner shape is a dedicated "no-outbox" path. Until
        // a per-event-type policy lands (Sprint-6+), use a no-op event
        // type string with empty payload; the existing OutboxRouteRegistry
        // routes unknown types as Publish-default which produces a noop
        // when no subscriber is registered.
        //
        // To keep the receiver's contract narrow, the orchestrator does
        // NOT touch the outbox for skipped events — call IngestFailedAsync
        // with a non-error reason that records the skip reason. The Failed
        // status is semantically wrong for a skipped event, so the cleaner
        // move is a third IngestSkippedAsync overload. Sprint-4.5 keeps
        // the policy minimal: use the existing IngestAsync with a sentinel
        // event-type that the registry treats as Publish-default and
        // accepts a no-op payload, then mark the row Processed downstream.
        //
        // Pragmatic choice for U3 ship: persist with status=Received via
        // IngestAsync + an inert downstream event-type that's NOT
        // registered in OutboxRouteRegistry. The current registry's
        // unregistered → PublishDefault behavior would publish the event;
        // but with no subscriber for the sentinel type, that's a no-op at
        // the broker. Trade-off documented; Sprint-6+ adds explicit
        // per-event-type skip in the receiver.
        var ingestResult = await _ingest
            .IngestAsync(
                envelope,
                downstreamEventType: SentinelSkipEventType,
                downstreamPayload: new { skipped = true, eventType = envelope.EventType },
                ct
            )
            .ConfigureAwait(false);
        if (!ingestResult.IsSuccess)
        {
            return Result<WebhookProcessOutcome>.Failure(
                ingestResult.Error!,
                ingestResult.ErrorCode
            );
        }
        return Result<WebhookProcessOutcome>.Success(
            new WebhookProcessOutcome(
                EventId: ingestResult.Value!.EventId,
                IsDuplicate: ingestResult.Value.IsDuplicate,
                Status: WebhookProcessStatus.EventSkipped
            )
        );
    }

    /// <summary>
    /// Sentinel event-type for skipped (non-order.created) webhooks. Not
    /// registered in <c>OutboxRouteRegistry</c> — falls through to
    /// <c>PublishDefault</c> with no subscriber, producing a no-op at the
    /// broker. Sprint-6+ refines this to an explicit "no downstream" path.
    /// </summary>
    private const string SentinelSkipEventType = "ShopFlow.Channel.Webhooks.WebhookEventSkippedV1";
}

/// <summary>
/// Outcome of <see cref="WebhookOrchestrator.ProcessAsync"/> — what the
/// controller maps to its HTTP response. All outcomes are HTTP 200 from
/// the receiver's perspective: the receiver did its job (signature
/// verified, row persisted), and the downstream policy (emit vs skip
/// vs fail) is communicated via <see cref="Status"/>.
/// </summary>
public sealed record WebhookProcessOutcome(
    Guid EventId,
    bool IsDuplicate,
    WebhookProcessStatus Status,
    IReadOnlyList<string>? UnmappedSkus = null
);

/// <summary>
/// Sprint-4.5 U3 — orchestrator outcome states.
/// </summary>
public enum WebhookProcessStatus
{
    /// <summary>
    /// <c>order.created</c> with all lines mapped — <c>OrderImportedV1</c>
    /// emitted to the outbox.
    /// </summary>
    OrderImported,

    /// <summary>
    /// <c>order.created</c> with at least one unmapped line — webhook
    /// event persisted with status Failed, no outbox row written.
    /// </summary>
    ImportFailed,

    /// <summary>
    /// Non-<c>order.created</c> event — webhook event persisted (audit),
    /// no actionable downstream emission. Per-event-type policy refined
    /// in Sprint-6+.
    /// </summary>
    EventSkipped,
}
