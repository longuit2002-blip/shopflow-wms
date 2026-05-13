namespace ShopFlow.Contracts.Inventory;

/// <summary>
/// Inventory-emitted result event when ALL lines of a
/// <c>ReserveStockV1</c> successfully reserved. The saga consumes this on
/// the AwaitingReservation → Reserved transition. Carries per-line outcome
/// detail so downstream listeners (Analytics, audit) can correlate the
/// inserted ledger rows back to the Outbound order lines.
/// </summary>
public sealed record StockReservedV1(
    Guid OrderId,
    Guid TenantId,
    IReadOnlyList<LineOutcomeV1> LineOutcomes,
    DateTime OccurredAt
);

/// <summary>
/// Per-line outcome for <see cref="StockReservedV1"/> and
/// <see cref="StockReservationFailedV1"/>. <see cref="ReservationId"/> is
/// the ledger row id on success (the diagnostic link from the Outbound
/// line back to the Inventory row); null on the per-line OVERSOLD case.
/// <see cref="Status"/> is the string form of
/// <c>ShopFlow.Inventory.Application.LineOutcomeStatus</c> — kept as a
/// string here so the contract doesn't take a domain dependency.
/// </summary>
public sealed record LineOutcomeV1(
    string OrderLineId,
    string Sku,
    Guid? ReservationId,
    string Status
);
