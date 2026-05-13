namespace ShopFlow.Contracts.Inventory;

/// <summary>
/// Inventory-emitted result event for the atomic-failure case of
/// <c>ReserveStockV1</c>: any line oversold, so ZERO ledger rows were
/// inserted (all-or-nothing semantics per K11). Carries per-line
/// PASS/OVERSOLD detail so the saga's compensation path can decide which
/// lines to release if a future change makes the reservation
/// non-atomic. In the current atomic shape the saga publishes
/// <c>ReleaseStockV1</c> with an empty <c>OrderLineIds</c> list (no-op
/// release) because nothing actually reserved.
/// </summary>
public sealed record StockReservationFailedV1(
    Guid OrderId,
    Guid TenantId,
    IReadOnlyList<LineOutcomeV1> LineOutcomes,
    DateTime OccurredAt
);
