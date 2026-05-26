using MassTransit;
using Microsoft.Extensions.DependencyInjection;
using ShopFlow.Contracts.Inventory;
using ShopFlow.Contracts.Outbound;
using ShopFlow.Outbound.Application.Ports;
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
/// <para>U4 shipped the saga skeleton — happy-path
/// <c>OrderPlacedV1 → AwaitingReservation → Reserved</c> and the
/// compensation entry on <c>StockReservationFailedV1</c>. U5 chained
/// the <c>Reserved → AwaitingPick</c> auto-transition with an
/// <c>IPickQueue.GetWriter(tenantId).WriteAsync</c> back-pressuring
/// write in the same Then handler. U6 wires controllers that publish
/// the in-process Pick/Pack/Ship events; U7 fills in the compensation
/// body (CompensatingReservation → Cancelled via <c>StockReleasedV1</c>
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
        // Sprint-12.5 U3 — Path C entry for ship-failure compensation.
        Event(() => ShipFailed, x => x.CorrelateById(ctx => ctx.Message.OrderId));

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
                .ThenAsync(ctx =>
                    RecordTransitionAsync(
                        ctx,
                        "Initial",
                        "AwaitingReservation",
                        nameof(OrderPlacedV1),
                        actorUserId: null
                    )
                )
                .Publish(ctx => new ReserveStockV1(
                    OrderId: ctx.Message.OrderId,
                    TenantId: ctx.Message.TenantId,
                    Lines: ctx.Message.Lines.Select(l => new ReserveStockLineV1(
                            l.OrderLineId,
                            l.Sku,
                            l.Qty
                        ))
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
                .TransitionTo(Reserved)
                .ThenAsync(ctx =>
                    RecordTransitionAsync(
                        ctx,
                        "AwaitingReservation",
                        "Reserved",
                        nameof(StockReservedV1),
                        actorUserId: null
                    )
                )
                // U5 — Reserved → AwaitingPick auto-transition. The Then
                // handler below writes one PickRequestV1 envelope to the
                // tenant's in-process Channel via IPickQueue, then the
                // chained TransitionTo(AwaitingPick) moves the state
                // machine forward in the same saga commit. Both effects
                // ride the message scope: the channel write is in-process
                // (no transactional coupling to the saga's DB row, which
                // is fine because the pick generator is at-most-once on
                // each PickRequestV1 and the saga's state column is the
                // authoritative pointer to AwaitingPick).
                .ThenAsync(async ctx =>
                {
                    // GetPayload<IServiceProvider> resolves the MT message
                    // scope DI — the IPickQueue is registered as Singleton
                    // (see AddOutboundModule) so the same registry is
                    // shared across consume scopes.
                    var sp = ctx.GetPayload<IServiceProvider>();
                    var queue = sp.GetRequiredService<IPickQueue>();

                    var request = new PickRequestV1(
                        OrderId: ctx.Saga.CorrelationId,
                        TenantId: ctx.Saga.TenantId,
                        ShippingProfile: ctx.Saga.ShippingProfile,
                        // DateTime.UtcNow is acceptable per the plan U5
                        // approach: the channel write is in-process, not a
                        // persisted timestamp; the wave generator reads
                        // EnqueuedAt to compute the sliding window's age.
                        // Tests inject a FakeTimeProvider on the generator
                        // side; here the saga has no TimeProvider seam
                        // (would require taking a DI dependency on the
                        // state machine class itself, which is global and
                        // singleton-shaped).
                        EnqueuedAt: DateTime.UtcNow,
                        LineCount: ctx.Saga.LineCount
                    );

                    var writer = queue.GetWriter(ctx.Saga.TenantId);
                    // WriteAsync back-pressures when the bounded channel
                    // is full — correctness wins over latency per the
                    // hard non-negotiable. The CancellationToken comes
                    // from the message context so the saga middleware's
                    // shutdown path bubbles cleanly.
                    await writer.WriteAsync(request, ctx.CancellationToken).ConfigureAwait(false);
                })
                .TransitionTo(AwaitingPick)
                .ThenAsync(ctx =>
                    RecordTransitionAsync(
                        ctx,
                        "Reserved",
                        "AwaitingPick",
                        nameof(StockReservedV1),
                        actorUserId: null
                    )
                ),
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
                .ThenAsync(ctx =>
                    RecordTransitionAsync(
                        ctx,
                        "AwaitingReservation",
                        "CompensatingReservation",
                        nameof(StockReservationFailedV1),
                        actorUserId: null
                    )
                )
        );

        // ---- Reserved transitions ----------------------------------------
        // U5 chained Reserved → AwaitingPick on the StockReserved Then
        // handler above — the state machine flows through Reserved as a
        // transient state, never parking there. The state property is
        // kept so MT's state column has a legal value mid-transition + so
        // U7 can add a When(StockReservationFailedEvent) handler in
        // Reserved state for the race case (concurrent failure-on-other-
        // line after success) without re-introducing the state.

        // ---- AwaitingPick transitions (U7 fills in compensation) --------
        During(
            AwaitingPick,
            When(PickConfirmed)
                .Then(ctx => ctx.Saga.UpdatedAt = DateTime.UtcNow)
                .TransitionTo(Picked)
                .ThenAsync(ctx =>
                    RecordTransitionAsync(
                        ctx,
                        "AwaitingPick",
                        "Picked",
                        nameof(PickConfirmed),
                        ctx.Message.ActorUserId
                    )
                ),
            // U7 Path B — pick failure. The StockReserved handler in the
            // AwaitingReservation block above already populated
            // ReservedLineSkus + LinesAwaitingRelease for this saga; the
            // WhenEnter(CompensatingReservation) activity below reads
            // those fields to decide whether to publish ReleaseStockV1
            // (Path B; LinesAwaitingRelease > 0) or short-circuit
            // straight to Cancelled (Path A; LinesAwaitingRelease == 0).
            When(PickFailed)
                .Then(ctx => ctx.Saga.UpdatedAt = DateTime.UtcNow)
                .TransitionTo(CompensatingReservation)
                .ThenAsync(ctx =>
                    RecordTransitionAsync(
                        ctx,
                        "AwaitingPick",
                        "CompensatingReservation",
                        nameof(PickFailed),
                        ctx.Message.ActorUserId
                    )
                )
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
                .ThenAsync(ctx =>
                    RecordTransitionAsync(
                        ctx,
                        "Picked",
                        "Packed",
                        nameof(PackConfirmed),
                        ctx.Message.ActorUserId
                    )
                )
        );

        During(
            Packed,
            // TODO U6: auto Packed → AwaitingShip when the controller flow
            // commits. For now ShipConfirmed transitions directly from
            // Packed → Shipped, matching the Order aggregate's state machine.
            When(ShipConfirmed)
                .Then(ctx => ctx.Saga.UpdatedAt = DateTime.UtcNow)
                .TransitionTo(Shipped)
                .ThenAsync(ctx =>
                    RecordTransitionAsync(
                        ctx,
                        "Packed",
                        "Shipped",
                        nameof(ShipConfirmed),
                        ctx.Message.ActorUserId
                    )
                ),
            // Sprint-12.5 U3 — Path C entry. Mirrors PickFailed precedent
            // structurally: the operator triggers Packed → CompensatingReservation;
            // the existing WhenEnter(CompensatingReservation, IfElse(...)) activity
            // handles the entry transparently because ReservedLineSkus +
            // LinesAwaitingRelease were populated on AwaitingReservation → Reserved
            // and survive through Reserved → AwaitingPick → Picked → Packed.
            When(ShipFailed)
                .Then(ctx => ctx.Saga.UpdatedAt = DateTime.UtcNow)
                .TransitionTo(CompensatingReservation)
                .ThenAsync(ctx =>
                    RecordTransitionAsync(
                        ctx,
                        "Packed",
                        "CompensatingReservation",
                        nameof(ShipFailed),
                        ctx.Message.ActorUserId
                    )
                )
        );

        // ---- CompensatingReservation transitions (U7) -------------------
        // OnEnter activity (MT 8.x WhenEnter shape): fires when ANY
        // transition lands the saga in CompensatingReservation. Two
        // entry paths exist (both share this activity):
        //   Path A — AwaitingReservation + StockReservationFailedV1 (atomic-
        //   CTE failure). ReservedLineSkus = "", LinesAwaitingRelease = 0;
        //   the IfElse branch transitions straight to Cancelled with no
        //   release publish (release-the-empty-set is a no-op).
        //   Path B — AwaitingPick + PickFailed (operator-reported pick
        //   failure). ReservedLineSkus = "L1,L2,...", LinesAwaitingRelease
        //   = count from the prior StockReserved handler. The IfElse branch
        //   publishes ONE ReleaseStockV1 envelope carrying the parsed line
        //   ids; the saga then waits for StockReleased arrivals to drain
        //   the counter.
        WhenEnter(
            CompensatingReservation,
            x =>
                x.IfElse(
                    // Condition: nothing to release ⇒ Path A.
                    ctx => ctx.Saga.LinesAwaitingRelease <= 0,
                    // Then-branch: Path A. No release publish; transition
                    // directly to Cancelled. The WhenEnter(Cancelled, ...)
                    // activity below publishes OrderCancelled to drive the
                    // Order row update.
                    then =>
                        then.TransitionTo(Cancelled)
                            .ThenAsync(ctx =>
                                RecordTransitionAsync(
                                    ctx,
                                    "CompensatingReservation",
                                    "Cancelled",
                                    "PathA_EmptyReleaseSet",
                                    actorUserId: null
                                )
                            ),
                    // Else-branch: Path B. Publish ONE ReleaseStockV1 with
                    // the OrderLineIds parsed from ReservedLineSkus. K13's
                    // accepted Publish-for-commands trade-off applies (saga
                    // doesn't have direct outbox access). Inventory's
                    // ReleaseStockConsumer applies the release + emits
                    // ONE StockReleasedV1 carrying the actually-released
                    // line ids; the StockReleased handler below uses that
                    // set to drain LinesAwaitingRelease via Set-based dedup.
                    @else =>
                        @else.Publish(ctx => new ReleaseStockV1(
                            OrderId: ctx.Saga.CorrelationId,
                            TenantId: ctx.Saga.TenantId,
                            OrderLineIds: ParseLineSkus(ctx.Saga.ReservedLineSkus)
                        ))
                )
        );

        During(
            CompensatingReservation,
            // Set-based dedup (K15 supplementary decision): on each
            // StockReleased arrival, add every line id in the payload to
            // ReleasedLineSkus IFF it's not already there; decrement
            // LinesAwaitingRelease ONCE per first-sighted line id. Guards
            // against MassTransit at-least-once redelivery driving the
            // counter negative. Once the counter hits zero, transition to
            // Cancelled. Subsequent StockReleased redeliveries are a no-op
            // because every line id is already in the set + the saga is
            // already at the Cancelled state (which has no handler for
            // StockReleased).
            When(StockReleased)
                .Then(ctx =>
                {
                    var saga = ctx.Saga;
                    var alreadyReleased = ParseLineSkusToSet(saga.ReleasedLineSkus);
                    foreach (var lineId in ctx.Message.OrderLineIds)
                    {
                        if (alreadyReleased.Add(lineId))
                        {
                            // First sight of this line id for this saga;
                            // credit the counter. Subsequent redeliveries
                            // hit the else branch + leave the counter alone.
                            saga.LinesAwaitingRelease--;
                        }
                    }
                    saga.ReleasedLineSkus = string.Join(",", alreadyReleased);
                    saga.UpdatedAt = DateTime.UtcNow;
                })
                .If(
                    // Guard: only transition when ALL expected releases have
                    // landed. Defensive <= 0 vs == 0 — protects against the
                    // theoretical case where ReleasedLineSkus accumulates
                    // MORE entries than LinesAwaitingRelease started at (a
                    // misbehaving consumer emitting StockReleasedV1 for a
                    // line that was never reserved would drive the counter
                    // negative without this).
                    ctx => ctx.Saga.LinesAwaitingRelease <= 0,
                    branch =>
                        branch
                            .TransitionTo(Cancelled)
                            .ThenAsync(ctx =>
                                RecordTransitionAsync(
                                    ctx,
                                    "CompensatingReservation",
                                    "Cancelled",
                                    nameof(StockReleasedV1),
                                    actorUserId: null
                                )
                            )
                )
        );

        // ---- Cancelled terminal state -----------------------------------
        // OnEnter activity publishes OrderCancelled (in-process saga event;
        // NOT a cross-module Contracts type) so the OrderCancelledConsumer
        // in Outbound.Infrastructure can flip the Order row's Status to
        // Cancelled. The R3 eventual-consistency boundary applies: the saga
        // commit and the Order row update live in separate EF transactions.
        WhenEnter(Cancelled, x => x.Publish(ctx => new OrderCancelled(ctx.Saga.CorrelationId)));

        // ---- Terminal states --------------------------------------------
        // SetCompletedWhenFinalized() would auto-delete the saga state row
        // on entering a Final state. We intentionally do NOT call it here
        // for Sprint-3-redux: keeping the terminal-state row supports
        // post-mortem queries + the scale-gate's after-the-fact assertions.
        // Both Shipped and Cancelled are "logical terminals" — no handler
        // declared in either state for any event, so MT treats stray events
        // as out-of-band and the saga sits put.
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
    public Event<ShipFailed> ShipFailed { get; } = null!;

    /// <summary>
    /// Parse the comma-separated <c>ReservedLineSkus</c> column into a
    /// stable-order list for the <see cref="ReleaseStockV1.OrderLineIds"/>
    /// payload. Empty/whitespace input yields an empty list.
    /// </summary>
    private static IReadOnlyList<string> ParseLineSkus(string commaSeparated)
    {
        if (string.IsNullOrWhiteSpace(commaSeparated))
        {
            return Array.Empty<string>();
        }
        return commaSeparated
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToArray();
    }

    /// <summary>
    /// Parse the comma-separated <c>ReleasedLineSkus</c> column into the
    /// mutable set used for the U7 dedup pass. Empty/whitespace input
    /// yields an empty set so the first <c>StockReleased</c> arrival adds
    /// every line id cleanly.
    /// </summary>
    private static HashSet<string> ParseLineSkusToSet(string commaSeparated)
    {
        if (string.IsNullOrWhiteSpace(commaSeparated))
        {
            return new HashSet<string>(StringComparer.Ordinal);
        }
        return new HashSet<string>(
            commaSeparated.Split(
                ',',
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries
            ),
            StringComparer.Ordinal
        );
    }

    /// <summary>
    /// Sprint-7 U2 — dispatch a state-transition record to the
    /// <see cref="SagaTransitionObserver"/>. Resolved from the MT consume
    /// scope's <see cref="IServiceProvider"/> so the observer's scoped
    /// dependencies (<see cref="IOrderTransitionRepository"/>,
    /// <see cref="IOutboundOutbox"/>) share the same per-tenant
    /// <c>OutboundDbContext</c> the saga's MT EF repository commits.
    /// </summary>
    /// <remarks>
    /// <para>Doc-review architectural decision: this static helper replaces
    /// MT's <c>IStateObserver&lt;T&gt;</c> interface mechanism (which is not
    /// reliably wired through <c>MassTransitStateMachine&lt;T&gt;</c> in
    /// MT 8.3.4) with explicit per-branch <c>.ThenAsync(...)</c> invocation.
    /// The class-shape decision is preserved (single observer class,
    /// comprehensive branch coverage) — including the <c>WhenEnter</c>
    /// <c>IfElse</c> Path A branch and the <c>If</c> StockReleased
    /// counter-drain branch — so the audit log captures every TransitionTo
    /// regardless of which DSL construct fired it.</para>
    /// </remarks>
    private static async Task RecordTransitionAsync(
        BehaviorContext<FulfillmentSagaState> ctx,
        string fromState,
        string toState,
        string eventType,
        Guid? actorUserId
    )
    {
        // GetService (nullable) instead of GetRequiredService: Sprint-3-redux's
        // FulfillmentSaga unit tests build the MT TestHarness without registering
        // the Sprint-7 observer, and re-wiring every legacy test to register a
        // no-op observer chain (IOrderTransitionRepository + IOutboundOutbox +
        // TimeProvider) would balloon the diff. Production registration lives in
        // OutboundServiceCollectionExtensions.AddOutboundModule (Sprint-7 U2) and
        // is guarded by the SagaTransitionObserverWiringTests integration test.
        // When the observer is missing in test scope, audit-write is a no-op.
        var sp = ctx.GetPayload<IServiceProvider>();
        var observer = sp.GetService<SagaTransitionObserver>();
        if (observer is null)
        {
            return;
        }
        await observer
            .RecordAsync(
                orderId: ctx.Saga.CorrelationId,
                tenantId: ctx.Saga.TenantId,
                fromState: fromState,
                toState: toState,
                eventType: eventType,
                actorUserId: actorUserId,
                ct: ctx.CancellationToken
            )
            .ConfigureAwait(false);
    }
}
