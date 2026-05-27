namespace ShopFlow.Outbound.Application.Sagas.Events;

/// <summary>
/// Sprint-13 U3 — in-process saga event published by the
/// <c>POST /mark-pack-failed</c> controller when a Packer reports the order
/// cannot be packed (typically an item damaged at the pack station,
/// discovered after pick-confirm but before pack-confirm). The saga's
/// <c>Picked → CompensatingReservation</c> transition (Path D) reads
/// <see cref="Reason"/> for diagnostic logging and reuses the Sprint-3-redux
/// Path B / Sprint-12.5 Path C compensation primitives — <c>ReservedLineSkus</c>
/// + <c>LinesAwaitingRelease</c> survive through to <c>Picked</c>, so the
/// existing <c>WhenEnter(CompensatingReservation, IfElse(...))</c> activity
/// handles Path D entry transparently without a new branch.
/// </summary>
/// <remarks>
/// Mirrors Sprint-3-redux's <c>PickFailed</c> + Sprint-12.5's
/// <c>ShipFailed</c> shape. Per Sprint-13 K1, the pre-state is <c>Picked</c>
/// (NOT <c>AwaitingPack</c> — the Order aggregate never rests there since
/// <c>ConfirmPackAsync</c> chains <c>MarkPacked → MarkAwaitingShip</c>
/// atomically). <see cref="ActorUserId"/> per Sprint-12.5 KTD3 carries the
/// operator (JWT subject) through to the audit row written by
/// <c>SagaTransitionObserver</c>; positional-default <c>null</c> preserves
/// backward compatibility with existing test ctors (Sprint-13 K6).
/// </remarks>
public sealed record PackFailed(Guid OrderId, string Reason, Guid? ActorUserId = null);
