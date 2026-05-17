namespace ShopFlow.StockSync.Application.Coalescing;

/// <summary>
/// Single bucket-value in the coalescing buffer (Sprint-5 plan U3).
/// Holds the latest <c>available_to_sell</c> reading for the
/// <see cref="CoalesceKey"/>, the wall-clock <see cref="ObservedAt"/>
/// stamped by the source <c>StockLevelChangedV1.OccurredAt</c>, and the
/// pre-resolved <see cref="IsFlashSale"/> bit (read by the consumer via
/// <c>ISkuFlagRepository</c> at upsert time so the flush path doesn't
/// re-hit the DB).
/// </summary>
/// <param name="AvailableToSell">Post-commit Inventory value to push.</param>
/// <param name="ObservedAt">Source event <c>OccurredAt</c>. Drives the
/// last-by-observed-time tiebreaker in <c>CoalescingBuffer.Upsert</c>.</param>
/// <param name="IsFlashSale">Whether the SKU routes to the high-priority
/// dispatch lane (Sprint-5 plan R10, queue split in U4).</param>
public sealed record CoalesceEntry(int AvailableToSell, DateTime ObservedAt, bool IsFlashSale);
