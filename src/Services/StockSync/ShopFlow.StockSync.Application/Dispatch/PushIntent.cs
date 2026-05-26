namespace ShopFlow.StockSync.Application.Dispatch;

/// <summary>
/// One coalesced stock-push instruction handed from the flush service to
/// the per-tenant dispatcher queue (Sprint-5 plan U3 → U4 hand-off).
/// </summary>
/// <remarks>
/// <para>One <see cref="PushIntent"/> represents the latest
/// <c>available_to_sell</c> reading for a single
/// <c>(tenant, sku, channel)</c> triple after the coalesce window
/// collapses every redundant update. The dispatcher (U5) materialises
/// this into a real <c>IChannelAdapter.PushStockUpdateAsync</c> call
/// guarded by the token bucket + Polly v8 breaker.</para>
///
/// <para><see cref="IdempotencyKey"/> is the deterministic dedup key the
/// dispatcher passes to the channel adapter so retries / breaker
/// re-emits don't double-publish to the marketplace. Format
/// <c>{TenantId}:{Sku}:{ChannelType}:{ObservedAt:O}</c> — same
/// <c>ObservedAt</c> = same intent = same key, regardless of how many
/// flushes it took to drain.</para>
/// </remarks>
/// <param name="TenantId">Owning tenant.</param>
/// <param name="Sku">Internal SKU.</param>
/// <param name="ChannelType">Target marketplace slug.</param>
/// <param name="Available">Verbatim post-commit <c>available_to_sell</c>.</param>
/// <param name="ObservedAt">Source event <c>OccurredAt</c>.</param>
/// <param name="IsFlashSale">Routes high-priority lane when true.</param>
/// <param name="IdempotencyKey">Deterministic dedup key.</param>
public sealed record PushIntent(
    Guid TenantId,
    string Sku,
    string ChannelType,
    int Available,
    DateTime ObservedAt,
    bool IsFlashSale,
    string IdempotencyKey
)
{
    /// <summary>
    /// Canonical idempotency-key format. Centralised so the consumer +
    /// dispatcher + push-log lookup agree on the spelling.
    /// </summary>
    public static string BuildIdempotencyKey(
        Guid tenantId,
        string sku,
        string channelType,
        DateTime observedAt
    ) => $"{tenantId}:{sku}:{channelType}:{observedAt:O}";
}
