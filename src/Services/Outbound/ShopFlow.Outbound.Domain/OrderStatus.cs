namespace ShopFlow.Outbound.Domain;

/// <summary>
/// Order lifecycle states per Sprint-3-redux plan R3 — mirrors the
/// fulfillment saga states. The saga (U4) is the source of truth for
/// transitions; controller endpoints (U6+) drive both the saga event
/// and the Order row's status in sequence per K12 in the plan.
/// </summary>
/// <remarks>
/// U1 ships the enum only; transition guards land in U2 when the Order
/// aggregate's state-machine methods fill in.
/// </remarks>
public enum OrderStatus
{
    Created = 0,
    AwaitingReservation = 1,
    Reserved = 2,
    AwaitingPick = 3,
    Picked = 4,
    AwaitingPack = 5,
    Packed = 6,
    AwaitingShip = 7,
    Shipped = 8,
    CompensatingReservation = 9,
    Cancelled = 10,
}
