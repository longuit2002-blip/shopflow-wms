namespace ShopFlow.Outbound.Application.Sagas.Events;

/// <summary>
/// In-process saga event published by the U6 <c>POST /confirm-ship</c>
/// controller after the mock shipping provider returns a label +
/// tracking number. The saga's AwaitingShip → Shipped transition publishes
/// <c>ConfirmStockV1</c> + <c>TrackingPushedV1</c> on this event; the
/// controller persists the carrier metadata to the orders row in its own
/// DbContext (R3 eventual-consistency boundary).
/// </summary>
/// <remarks>
/// Sprint-3-redux U4 ships the type so the state machine compiles; U6
/// wires the controller publish + the ConfirmStockV1 outbox enqueue.
/// </remarks>
public sealed record ShipConfirmed(Guid OrderId, string LabelUrl, string TrackingNumber, Guid? ActorUserId = null);
