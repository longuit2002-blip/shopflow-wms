namespace ShopFlow.Outbound.Application.Sagas.Events;

/// <summary>
/// In-process saga event published by the U6 <c>POST /confirm-pick</c>
/// controller after the picker reports completion. NOT a MassTransit
/// cross-module contract (no <c>V1</c> suffix; lives in Application, not
/// <c>ShopFlow.Contracts</c>) — the saga listens for it on the in-memory
/// bus inside the Outbound process. The OrderId correlates to the saga
/// state row created on <c>OrderPlacedV1</c>.
/// </summary>
/// <remarks>
/// Sprint-3-redux U4 ships the type so the state machine compiles; U6
/// wires the controller publish. The saga's AwaitingPick → Picked
/// transition reads no fields off this event other than the correlation
/// id, so the payload is intentionally minimal.
/// </remarks>
public sealed record PickConfirmed(Guid OrderId, Guid? ActorUserId = null);
