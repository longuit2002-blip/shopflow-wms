namespace ShopFlow.Contracts.Inventory;

/// <summary>
/// Canonical cross-module event signaling that the published
/// <c>available_to_sell</c> count for a SKU has changed inside a tenant DB.
/// Sprint-5 KTD1 — wires Inventory's existing <c>StockChangedEvent</c>
/// (domain event) into a cross-module contract for the StockSync engine.
/// </summary>
/// <remarks>
/// <para>Emitted by <c>ReservationRepository</c> after every successful
/// reserve / release / confirm transition, and by
/// <c>StockItemRepository.AdjustAtBinAsync</c> after every successful
/// put-away delta. One row per affected SKU per commit.</para>
/// <para>The StockSync engine consumes this event (Sprint-5 U3 consumer)
/// and routes the latest value into its coalescing buffer per
/// <c>(tenant, sku, channel)</c>. Downstream of coalesce, the dispatcher
/// pushes <c>AvailableToSell</c> verbatim to every active channel adapter
/// (mirror-all allocation per Sprint-5 plan R5).</para>
/// <para><see cref="AvailableToSell"/> is the post-commit value of
/// <c>stock_items.available</c> — i.e., <c>on_hand - sum(active reservations)</c>.
/// Reservation ledger atomicity (Sprint-1-redux) keeps this consistent at
/// commit time; the consumer must not recompute.</para>
/// </remarks>
public sealed record StockLevelChangedV1(
    Guid TenantId,
    string Sku,
    int AvailableToSell,
    DateTime OccurredAt
);
