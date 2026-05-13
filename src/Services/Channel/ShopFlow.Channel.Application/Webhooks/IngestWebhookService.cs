using ShopFlow.Channel.Application.Ports;
using ShopFlow.Channel.Domain.Webhooks;
using ShopFlow.SharedKernel.Application;
using ShopFlow.SharedKernel.Domain;

namespace ShopFlow.Channel.Application.Webhooks;

/// <summary>
/// Orchestrates the per-tenant webhook ingress flow per Sprint-4 plan R3/R4
/// + U3. Run by <c>WebhooksController</c> after HMAC verification +
/// tenant-context binding:
/// <list type="number">
///   <item><description>Build a domain <see cref="WebhookEvent"/> from the parsed envelope.</description></item>
///   <item><description>Idempotent insert via <see cref="IWebhookEventRepository.TryInsertAsync"/> — UNIQUE-23505 catch returns the existing row on replay.</description></item>
///   <item><description>On first-write only, append the downstream outbox row (Sprint-4 U8 swaps the placeholder eventType for <c>OrderImportedV1</c>).</description></item>
///   <item><description>Single <see cref="IUnitOfWork.SaveChangesAsync"/> commits both rows atomically.</description></item>
/// </list>
/// </summary>
/// <remarks>
/// <para>Replay path: when the repository returns
/// <see cref="TryInsertWebhookResult.IsDuplicate"/> = true the orchestrator
/// short-circuits — <em>no</em> second outbox row is written, regardless of
/// commit timing. This is the load-bearing invariant the R10 scale-gate
/// asserts (same webhook replayed 100× → exactly one downstream order).</para>
/// <para>The orchestrator stamps the call site with
/// <see cref="IRequestContext"/> implicitly via the ambient tenant binding;
/// the controller binds it after the <c>IChannelDirectory</c> lookup
/// succeeds.</para>
/// </remarks>
public sealed class IngestWebhookService
{
    private readonly IWebhookEventRepository _webhookEvents;
    private readonly IChannelOutbox _outbox;
    private readonly IUnitOfWork _uow;

    public IngestWebhookService(
        IWebhookEventRepository webhookEvents,
        IChannelOutbox outbox,
        IUnitOfWork uow
    )
    {
        _webhookEvents = webhookEvents;
        _outbox = outbox;
        _uow = uow;
    }

    /// <summary>
    /// Ingest a verified-signature webhook. Caller has already (a) resolved
    /// the channel via <c>IChannelDirectory</c>, (b) verified the HMAC, and
    /// (c) bound the tenant context. This method opens no transaction
    /// explicitly — <see cref="IUnitOfWork.SaveChangesAsync"/> commits both
    /// the webhook_events insert and the channel_outbox_messages append in
    /// one EF transaction.
    /// </summary>
    /// <param name="envelope">Adapter-parsed envelope.</param>
    /// <param name="downstreamEventType">
    /// Wire-format event type written to <c>channel_outbox_messages.event_type</c>.
    /// U3 ships with a placeholder; U8 substitutes the real
    /// <c>OrderImportedV1.AssemblyQualifiedName</c>.
    /// </param>
    /// <param name="downstreamPayload">
    /// Object serialized to <c>channel_outbox_messages.payload</c> via
    /// <c>OutboxJsonOptions.Default</c> in the outbox adapter.
    /// </param>
    public async Task<Result<IngestWebhookResult>> IngestAsync(
        WebhookEnvelope envelope,
        string downstreamEventType,
        object downstreamPayload,
        CancellationToken ct
    )
    {
        ArgumentNullException.ThrowIfNull(envelope);
        ArgumentException.ThrowIfNullOrWhiteSpace(downstreamEventType);
        ArgumentNullException.ThrowIfNull(downstreamPayload);

        var providerEventIdResult = ProviderEventId.Create(envelope.ProviderEventId);
        if (!providerEventIdResult.IsSuccess)
        {
            return Result<IngestWebhookResult>.Failure(
                providerEventIdResult.Error!,
                providerEventIdResult.ErrorCode
            );
        }

        var eventResult = WebhookEvent.Create(
            envelope.ChannelId,
            providerEventIdResult.Value!,
            envelope.RawPayload,
            signatureVerified: true
        );
        if (!eventResult.IsSuccess)
        {
            return Result<IngestWebhookResult>.Failure(
                eventResult.Error!,
                eventResult.ErrorCode
            );
        }

        var insertResult = await _webhookEvents
            .TryInsertAsync(eventResult.Value!, ct)
            .ConfigureAwait(false);

        if (!insertResult.IsSuccess)
        {
            return Result<IngestWebhookResult>.Failure(
                insertResult.Error!,
                insertResult.ErrorCode
            );
        }

        var outcome = insertResult.Value!;

        // First-write only: enqueue the downstream outbox row. On replay,
        // the previously-committed outbox row is sufficient — writing a
        // second one would violate the R3 "exactly one downstream order
        // per replay" invariant.
        if (!outcome.IsDuplicate)
        {
            await _outbox
                .AppendAsync(downstreamEventType, downstreamPayload, ct)
                .ConfigureAwait(false);
            await _uow.SaveChangesAsync(ct).ConfigureAwait(false);
        }

        return Result<IngestWebhookResult>.Success(
            new IngestWebhookResult(outcome.EventId, outcome.IsDuplicate)
        );
    }
}
