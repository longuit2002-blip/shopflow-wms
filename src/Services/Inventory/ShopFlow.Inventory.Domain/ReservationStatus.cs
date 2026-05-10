namespace ShopFlow.Inventory.Domain;

/// <summary>
/// Reservation lifecycle states. The lifecycle is
/// <c>Active → (Confirmed | Released | Expired)</c>; rows are never deleted
/// from <c>reservations_ledger</c> (audit immutability — see
/// <c>src/Services/Inventory/AGENTS.md</c>).
/// </summary>
/// <remarks>
/// Numeric ordering is load-bearing for migration column constraints — do
/// not reorder without a follow-up migration that re-maps existing data.
/// </remarks>
public enum ReservationStatus
{
    /// <summary>Live reservation; counted against available stock.</summary>
    Active = 1,

    /// <summary>Fulfilled: stock_items.total_qty has been deducted.</summary>
    Confirmed = 2,

    /// <summary>Cancelled by the customer or upstream saga.</summary>
    Released = 3,

    /// <summary>Expired — the 15-minute window elapsed without confirmation.</summary>
    Expired = 4,
}
