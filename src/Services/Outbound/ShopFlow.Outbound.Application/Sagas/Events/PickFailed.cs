namespace ShopFlow.Outbound.Application.Sagas.Events;

/// <summary>
/// In-process saga event published by the U7 <c>POST /mark-pick-failed</c>
/// controller when an operator reports the order cannot be picked
/// (typically a physical stock discrepancy discovered at the bin). The
/// saga's AwaitingPick → CompensatingReservation transition reads the
/// reason for diagnostic logging and publishes <c>ReleaseStockV1</c> for
/// the lines that DID reserve (tracked in saga state).
/// </summary>
/// <remarks>
/// Sprint-3-redux U4 ships the type so the state machine compiles; U7
/// wires the controller publish + the compensation transition body.
/// </remarks>
public sealed record PickFailed(Guid OrderId, string Reason, Guid? ActorUserId = null);
