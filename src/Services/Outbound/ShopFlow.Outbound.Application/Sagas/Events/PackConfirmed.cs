namespace ShopFlow.Outbound.Application.Sagas.Events;

/// <summary>
/// In-process saga event published by the U6 <c>POST /confirm-pack</c>
/// controller after the weight check on the packed order. The saga's
/// Picked → Packed transition uses <see cref="ActualWeightTotal"/> only
/// for diagnostic capture; the controller persists the value to the
/// orders row in its own DbContext (R3 eventual-consistency boundary).
/// </summary>
/// <remarks>
/// Sprint-3-redux U4 ships the type so the state machine compiles; U6
/// wires the controller publish.
/// </remarks>
public sealed record PackConfirmed(Guid OrderId, int ActualWeightTotal, Guid? ActorUserId = null);
