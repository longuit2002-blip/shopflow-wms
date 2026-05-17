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
/// <para>U4 hand-off note: implementation routes
/// <see cref="PushIntent.IsFlashSale"/> = true onto the high-priority
/// lane and uses <c>BoundedChannelFullMode.DropOldest</c> on both lanes
/// so flash-sale traffic can't starve and normal traffic doesn't back up
/// the writer (coalescing already collapsed redundant updates upstream).</para>
///
/// <para>U5 hand-off note: <see cref="ReadNextAsync"/> is the consumer
/// side. The dispatcher BackgroundService loops one call per tenant +
/// awaits the next intent; the implementation guarantees strict
/// high-lane priority even when both lanes become readable in the same
/// scheduler tick.</para>
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

    /// <summary>
    /// Awaits the next <see cref="PushIntent"/> for
    /// <paramref name="tenantId"/>, preferring the high-priority lane.
    /// Blocks until either lane has an item or
    /// <paramref name="ct"/> fires. Strict priority: if both lanes are
    /// readable, the high lane wins.
    /// </summary>
    /// <exception cref="OperationCanceledException">
    /// Thrown when <paramref name="ct"/> is cancelled before an item
    /// becomes available.
    /// </exception>
    ValueTask<PushIntent> ReadNextAsync(Guid tenantId, CancellationToken ct);
}
