namespace ShopFlow.StockSync.Application.Dispatch;

/// <summary>
/// Port the <c>CoalesceFlushService</c> writes coalesced
/// <see cref="PushIntent"/> records into for downstream dispatch
/// (Sprint-5 plan U4). The implementation owns the per-tenant
/// high-priority + normal-priority <c>Channel&lt;PushIntent&gt;</c>
/// pair and the noisy-neighbor token-bucket guard.
/// </summary>
/// <remarks>
/// <para>U3 only writes here — U4 reads. Splitting the port keeps the
/// flush path completely unaware of priority lanes / rate limiting.</para>
///
/// <para>U4 hand-off note: implementation must route
/// <see cref="PushIntent.IsFlashSale"/> = true onto the high-priority
/// channel and apply backpressure semantics (BoundedChannelFullMode)
/// that prefer drop-newest on the normal lane so flash-sale traffic
/// can't starve.</para>
/// </remarks>
public interface IPerTenantQueue
{
    /// <summary>
    /// Enqueues <paramref name="intent"/> for downstream push. Returns
    /// without blocking the caller — the implementation may drop the
    /// item when its bounded buffer is saturated (backpressure metric
    /// surfaces via U8 diagnostics). The flush service does not retry.
    /// </summary>
    ValueTask EnqueueAsync(PushIntent intent, CancellationToken ct);
}
