namespace ShopFlow.Outbound.Application.Sagas.Events;

/// <summary>
/// Sprint-12.5 U3 — in-process saga event published by the
/// <c>POST /mark-ship-failed</c> controller when an operator reports the
/// order cannot ship (typically a carrier rejection or pre-loading damage).
/// The saga's <c>Packed → CompensatingReservation</c> transition (Path C)
/// reads <see cref="Reason"/> for diagnostic logging and reuses the
/// Sprint-3-redux Path B compensation primitives — <c>ReservedLineSkus</c>
/// + <c>LinesAwaitingRelease</c> survive through to Packed, so the existing
/// <c>WhenEnter(CompensatingReservation, IfElse(...))</c> activity handles
/// Path C entry transparently without a new branch.
/// </summary>
/// <remarks>
/// Mirrors Sprint-3-redux's <c>PickFailed</c> shape. <see cref="ActorUserId"/>
/// per Sprint-12.5 KTD3 carries the operator (JWT subject) through to the
/// audit row written by <see cref="SagaTransitionObserver"/>.
/// </remarks>
public sealed record ShipFailed(Guid OrderId, string Reason, Guid? ActorUserId = null);
