using ShopFlow.Channel.Application.Webhooks;
using ShopFlow.SharedKernel.Domain;

namespace ShopFlow.Channel.Application.Adapters;

/// <summary>
/// Per-marketplace adapter surface per Sprint-4 plan R1/U5. Implementations
/// live in <c>ShopFlow.Channel.Infrastructure.Adapters</c> and are resolved
/// by channel type via <see cref="IChannelAdapterFactory"/>. The Lazada
/// addition in Sprint-6 will require zero touches outside
/// <c>Adapters/Lazada/</c> + one DI registration line — the surface here
/// must be marketplace-agnostic.
/// </summary>
/// <remarks>
/// <para>Three responsibilities:</para>
/// <list type="number">
///   <item><description>Inbound envelope parse: marketplace-shape bytes → canonical <see cref="WebhookEnvelope"/>.</description></item>
///   <item><description>Inbound order-shape extraction (Sprint-4.5 U1): canonical envelope → marketplace-agnostic <see cref="ExternalOrderDraft"/>. The orchestrator resolves the draft's external SKUs to internal SKUs via <c>IProductMappingService</c> and assembles the canonical <c>OrderImportedV1</c>.</description></item>
///   <item><description>Outbound: push a stock update back to the marketplace (Sprint-5 wires the body; Sprint-4 ships the signature only).</description></item>
/// </list>
/// <para>State (rate-limit counters, circuit-breaker state) is owned by the
/// sync engine (Sprint-5), not the adapter. The adapter is stateless and
/// safely registered as Singleton.</para>
/// </remarks>
public interface IChannelAdapter
{
    /// <summary>
    /// Lower-case channel type identifier (e.g. <c>"shopee"</c>). Used by
    /// the factory to route inbound webhooks + outbound stock pushes.
    /// </summary>
    string ChannelType { get; }

    /// <summary>
    /// Parse raw marketplace-shape webhook bytes into the normalised
    /// envelope. The receiver invokes this after HMAC verification has
    /// passed; the body bytes are the same bytes that were signed.
    /// </summary>
    Result<WebhookEnvelope> ParseWebhook(
        Guid channelId,
        ReadOnlySpan<byte> body,
        IReadOnlyDictionary<string, string> headers
    );

    /// <summary>
    /// Extract the marketplace-shape order data from an
    /// <c>order.created</c> envelope per Sprint-4.5 plan U1. Caller is
    /// responsible for gating on <see cref="WebhookEnvelope.EventType"/>;
    /// the adapter returns <see cref="Result{T}.Failure"/> with code
    /// <c>shopee.order.event_type_unsupported</c> (or marketplace
    /// equivalent) on a non-<c>order.created</c> envelope.
    /// </summary>
    /// <remarks>
    /// <para>Returns <see cref="Result{T}.Failure"/> on malformed input,
    /// missing required fields (order id, items, per-line SKU), or
    /// non-positive line quantities. Never throws on bad input — the
    /// receiver maps the failure code to a 400-shape response.</para>
    /// <para>The adapter does not resolve external SKUs to internal SKUs
    /// — that is the orchestrator's job. The draft carries raw
    /// marketplace SKUs verbatim.</para>
    /// </remarks>
    Result<ExternalOrderDraft> ParseOrderCreated(WebhookEnvelope envelope);

    /// <summary>
    /// Push a stock-level update to the marketplace. Sprint-4 plan U5 ships
    /// this as a deferred stub (returns Result.Failure with
    /// "sprint-5-deferred" error code); the body wires in Sprint-5
    /// alongside the sync engine.
    /// </summary>
    Task<Result> PushStockUpdateAsync(StockUpdateRequest request, CancellationToken ct);
}

/// <summary>
/// Stock-push payload for <see cref="IChannelAdapter.PushStockUpdateAsync"/>.
/// Sprint-5 fleshes out the fields the sync engine produces; for U5 this
/// is the placeholder shape so the interface compiles.
/// </summary>
public sealed record StockUpdateRequest(
    Guid ChannelId,
    string ExternalSku,
    int Quantity,
    DateTime ObservedAt,
    string IdempotencyKey
);
