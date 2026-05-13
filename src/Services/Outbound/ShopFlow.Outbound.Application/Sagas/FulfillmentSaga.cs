using MassTransit;
using ShopFlow.Contracts.Inventory;
using ShopFlow.Contracts.Outbound;
using ShopFlow.Outbound.Application.Sagas.Events;

namespace ShopFlow.Outbound.Application.Sagas;

/// <summary>
/// Sprint-3-redux U4 — fulfillment state-machine saga. One saga instance
/// per customer order. Drives the order through the
/// Reserve → Pick → Pack → Ship pipeline (11 states: 9 transient + 2
/// terminal). Per K1/K2 the saga correlates by <c>OrderId</c> directly
/// and consumes ONE multi-line <c>ReserveStockV1</c> envelope per order
/// (not one per line).
/// </summary>
/// <remarks>
/// <para>This unit ships the saga skeleton — happy-path
/// <c>OrderPlacedV1 → AwaitingReservation → Reserved</c> and the
/// compensation entry on <c>StockReservationFailedV1</c>. U5 wires the
/// <c>IPickQueue</c> write that drives <c>Reserved → AwaitingPick</c>;
/// U6 wires controllers that publish the in-process Pick/Pack/Ship
/// events; U7 fills in the compensation body
/// (CompensatingReservation → Cancelled via <c>StockReleasedV1</c>
/// arrivals with Set-based dedup).</para>
///
/// <para>State-machine DSL note: each <see cref="MassTransitStateMachine{T}.Event{TMessage}"/>
/// declaration here pairs the typed message with a CorrelateById expression
/// pointing at <c>OrderId</c> on the message payload. <c>InstanceState</c>
/// maps the saga state column to the named-state strings of the declared
/// <see cref="State"/> properties — this is the v8.x default shape (string
/// column), which matches the U1 migration's <c>"CurrentState" text</c>.</para>
///
/// <para>Per-tenant DbContext binding (K12) is handled at the
/// Infrastructure layer (<c>TenantBindingSagaFilter</c> +
/// <c>TenantAwareSagaDbContextFactory</c>) — the state machine itself is
/// tenant-agnostic; it reads <see cref="FulfillmentSagaState.TenantId"/>
/// off the saga state row when re-emitting cross-module commands.</para>
/// </remarks>
public sealed class FulfillmentSaga : MassTransitStateMachine<FulfillmentSagaState>
{
    public FulfillmentSaga()
    {
        InstanceState(s => s.CurrentState);

        // ---- Event declarations (CorrelateById on OrderId) ---------------
        Event(() => OrderPlaced, x => x.CorrelateById(ctx => ctx.Message.OrderId));
        Event(() => StockReserved, x => x.CorrelateById(ctx => ctx.Message.OrderId));
        Event(() => StockReservationFailedEvent, x => x.CorrelateById(ctx => ctx.Message.OrderId));
        Event(() => StockReleased, x => x.CorrelateById(ctx => ctx.Message.OrderId));
        Event(() => PickConfirmed, x => x.CorrelateById(ctx => ctx.Message.OrderId));
        Event(() => PickFailed, x => x.CorrelateById(ctx => ctx.Message.OrderId));
        Event(() => PackConfirmed, x => x.CorrelateById(ctx => ctx.Message.OrderId));
        Event(() => ShipConfirmed, x => x.CorrelateById(ctx => ctx.Message.OrderId));

        // ---- Initial transition: OrderPlacedV1 → AwaitingReservation -----
        // Publishing happens via .Publish<T>(ctx => ...) returning the new
        // message (MT 8.x preferred shape). .PublishAsync(ctx.Init<T>(...))
        // works on the bus but trips the in-memory test harness's saga
        // resolution path (it expects a sync-built message factory here).
        Initially(
            When(OrderPlaced)
                .Then(ctx =>
                {
                    ctx.Saga.CorrelationId = ctx.Message.OrderId;
                    ctx.Saga.TenantId = ctx.Message.TenantId;
                    ctx.Saga.ShippingProfile = ctx.Message.ShippingProfile;
                    ctx.Saga.LineCount = ctx.Message.Lines.Count;
                    ctx.Saga.UpdatedAt = DateTime.UtcNow;
                })
                .TransitionTo(AwaitingReservation)
                .Publish(ctx => new ReserveStockV1(
                    OrderId: ctx.Message.OrderId,
                    TenantId: ctx.Message.TenantId,
                    Lines: ctx.Message
                        .Lines.Select(l => new ReserveStockLineV1(l.OrderLineId, l.Sku, l.Qty))
                        .ToArray(),
                    // Default reservation TTL 15 min per Tech Design v3.0 §4.2.
                    // Per-shipping-profile tuning lands in Phase-2.
                    Ttl: TimeSpan.FromMinutes(15)
                ))
        );

        // ---- AwaitingReservation transitions ------------------------------
        During(
            AwaitingReservation,
            When(StockReserved)
                .Then(ctx =>
                {
                    // Capture which lines actually reserved — U7's compensation
                    // path uses this set to construct ReleaseStockV1.
                    ctx.Saga.ReservedLineSkus = string.Join(
                        ",",
                        ctx.Message.LineOutcomes.Select(o => o.OrderLineId)
                    );
                    ctx.Saga.LinesAwaitingRelease = ctx.Message.LineOutcomes.Count;
                    ctx.Saga.UpdatedAt = DateTime.UtcNow;
                })
                .TransitionTo(Reserved),
            // TODO U5: chain Reserved → AwaitingPick auto-transition with
            // IPickQueue.GetWriter(tenantId).TryWrite(new PickRequestV1(...))
            // in the same Then handler above. For U4 the saga parks at Reserved.
            When(StockReservationFailedEvent)
                .Then(ctx =>
                {
                    // Atomic-CTE failure → 0 ledger rows inserted; nothing to
                    // release. The saga still transitions through the
                    // CompensatingReservation state for diagnostic clarity;
                    // U7 will fast-track CompensatingReservation → Cancelled
                    // when ReservedLineSkus is empty (release-the-empty-set).
                    ctx.Saga.ReservedLineSkus = string.Empty;
                    ctx.Saga.LinesAwaitingRelease = 0;
                    ctx.Saga.UpdatedAt = DateTime.UtcNow;
                })
                .TransitionTo(CompensatingReservation)
        );

        // ---- Reserved transitions (mostly U5/U7 territory) ---------------
        // U5 will add: TransitionTo(AwaitingPick) chained off StockReserved Then.
        // U7 will add: When(StockReservationFailedEvent) handler in Reserved
        //   state (race: concurrent failure-on-other-line after success).
        // For U4 the Reserved state exists but has no inbound transitions
        // from outside (PickConfirmed during Reserved is out-of-band and
        // ignored — MT default behavior).

        // ---- AwaitingPick transitions (U5/U7 territory) ------------------
        During(
            AwaitingPick,
            When(PickConfirmed)
                .Then(ctx => ctx.Saga.UpdatedAt = DateTime.UtcNow)
                .TransitionTo(Picked),
            // PickFailed enters the compensation path; U7 fleshes out the
            // ReleaseStockV1 publish + dedup-counter wiring below.
            When(PickFailed)
                .Then(ctx => ctx.Saga.UpdatedAt = DateTime.UtcNow)
                .TransitionTo(CompensatingReservation)
        );

        // ---- Picked / AwaitingPack / Packed / AwaitingShip / Shipped ----
        // The auto-transition Picked → AwaitingPack is U6 (controller flow);
        // for U4 the Picked → AwaitingPack handoff is conceptual.
        During(
            Picked,
            When(PackConfirmed)
                .Then(ctx =>
                {
                    // Diagnostic: actual weight is persisted by the controller
                    // on the orders row; saga only captures the transition.
                    ctx.Saga.UpdatedAt = DateTime.UtcNow;
                })
                .TransitionTo(Packed)
        );

        During(
            Packed,
            // TODO U6: auto Packed → AwaitingShip when the controller flow
            // commits. For now ShipConfirmed transitions directly from
            // Packed → Shipped, matching the Order aggregate's state machine.
            When(ShipConfirmed)
                .Then(ctx => ctx.Saga.UpdatedAt = DateTime.UtcNow)
                .TransitionTo(Shipped)
        );

        // ---- CompensatingReservation transitions (U7 fleshes out) -------
        // TODO U7: On enter, publish ReleaseStockV1 with OrderLineIds set
        // from ReservedLineSkus. On StockReleased arrival, run the Set-based
        // dedup against ReleasedLineSkus + decrement LinesAwaitingRelease;
        // when zero → TransitionTo(Cancelled).

        // ---- Terminal states --------------------------------------------
        // SetCompletedWhenFinalized() would auto-delete the saga state row
        // on entering a Final state. We intentionally do NOT call it here
        // for Sprint-3-redux: keeping the terminal-state row supports
        // post-mortem queries + the scale-gate's after-the-fact assertions.
    }

    public State AwaitingReservation { get; } = null!;
    public State Reserved { get; } = null!;
    public State AwaitingPick { get; } = null!;
    public State Picked { get; } = null!;
    public State AwaitingPack { get; } = null!;
    public State Packed { get; } = null!;
    public State AwaitingShip { get; } = null!;
    public State Shipped { get; } = null!;
    public State CompensatingReservation { get; } = null!;
    public State Cancelled { get; } = null!;

    public Event<OrderPlacedV1> OrderPlaced { get; } = null!;
    public Event<StockReservedV1> StockReserved { get; } = null!;
    public Event<StockReservationFailedV1> StockReservationFailedEvent { get; } = null!;
    public Event<StockReleasedV1> StockReleased { get; } = null!;
    public Event<PickConfirmed> PickConfirmed { get; } = null!;
    public Event<PickFailed> PickFailed { get; } = null!;
    public Event<PackConfirmed> PackConfirmed { get; } = null!;
    public Event<ShipConfirmed> ShipConfirmed { get; } = null!;
}
