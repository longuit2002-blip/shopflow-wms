using ShopFlow.Inventory.Domain;

namespace ShopFlow.Inventory.Application;

/// <summary>
/// Per-line reservation request for the multi-line entry point on
/// <see cref="Ports.IReservationRepository.TryReserveLinesAsync"/> per
/// Sprint-3-redux K11. The shared <c>order_id</c> lives on the outer call;
/// each <see cref="LineReservation"/> carries its own
/// <see cref="OrderLineId"/> so the all-or-nothing CTE can INSERT N
/// ledger rows under the composite UNIQUE <c>(order_id, order_line_id)</c>.
/// </summary>
public sealed record LineReservation(Sku Sku, string OrderLineId, Quantity Quantity);

/// <summary>
/// Per-line outcome reported by <see cref="Ports.IReservationRepository.TryReserveLinesAsync"/>
/// — used on both success (one outcome per inserted row) and atomic-failure
/// (one outcome per requested line with <see cref="Status"/> indicating
/// whether the line individually had enough stock; helps the saga decide
/// the compensation set even though no rows actually inserted).
/// </summary>
public sealed record LineOutcome(
    string OrderLineId,
    Sku Sku,
    Guid? ReservationId,
    LineOutcomeStatus Status
);

/// <summary>
/// Result enum for <see cref="LineOutcome.Status"/>. <see cref="Reserved"/>
/// = the line did/would reserve; <see cref="Oversold"/> = the line's
/// requested quantity exceeded <c>stock_items.available</c> at the time
/// of the CTE check.
/// </summary>
public enum LineOutcomeStatus
{
    Reserved = 0,
    Oversold = 1,
}
