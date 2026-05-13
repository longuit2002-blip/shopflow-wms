namespace ShopFlow.Contracts.Inventory;

/// <summary>
/// Saga-issued command for the compensation path per Sprint-3-redux U7.
/// When <see cref="OrderLineIds"/> is empty, the consumer interprets it as
/// "release all Pending rows for <see cref="OrderId"/>" (full release);
/// otherwise the consumer issues a partial-set release through
/// <c>ReleaseLinesAsync</c> against only the listed line ids.
/// </summary>
/// <remarks>
/// The partial-set path supports the compensation case where some lines
/// successfully reserved before another line in the same order failed —
/// only the actually-reserved lines need releasing. The saga tracks which
/// lines reserved via the <c>StockReservedV1</c> outcomes; on a
/// <c>StockReservationFailedV1</c> for the rest, it publishes
/// <see cref="ReleaseStockV1"/> with that successful subset.
/// </remarks>
public sealed record ReleaseStockV1(
    Guid OrderId,
    Guid TenantId,
    IReadOnlyList<string> OrderLineIds
);
