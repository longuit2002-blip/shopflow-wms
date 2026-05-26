using MassTransit;
using MassTransit.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using ShopFlow.Contracts.Inventory;
using ShopFlow.Contracts.Outbound;
using ShopFlow.Outbound.Application.Ports;
using ShopFlow.Outbound.Application.Sagas;
using ShopFlow.Outbound.Application.Sagas.Events;
using PickQueueImpl = ShopFlow.Outbound.Infrastructure.PickQueue.PickQueue;

namespace ShopFlow.Outbound.UnitTests.Sagas;

/// <summary>
/// Sprint-3-redux U7 — pick-failure compensation path through the
/// <see cref="FulfillmentSaga"/>. Exercises the
/// <c>AwaitingPick + PickFailed → CompensatingReservation</c> +
/// <c>CompensatingReservation + StockReleasedV1 → Cancelled</c>
/// transitions plus the Path A (atomic-reservation-fail) short-circuit
/// where <c>LinesAwaitingRelease == 0</c> drives the saga straight from
/// <c>AwaitingReservation</c> to <c>Cancelled</c> via the
/// <c>CompensatingReservation</c> on-enter activity's IfElse branch.
/// </summary>
/// <remarks>
/// <para>The Set-based dedup (K15 supplementary decision) is the load-bearing
/// invariant for U8's scale gate: a redelivered <c>StockReleasedV1</c> must
/// NOT decrement <c>LinesAwaitingRelease</c> a second time (the counter
/// would go negative + the saga would prematurely transition to Cancelled
/// while the in-flight release was still being applied). Tests cover both
/// the happy path (single delivery: counter 2 → 0 → Cancelled) and the
/// redelivery case (counter 2 → 0 → Cancelled; second delivery is a no-op).</para>
///
/// <para>Path A vs Path B: Path A (atomic-reservation-fail) skips the
/// <c>ReleaseStockV1</c> publish because the underlying CTE didn't insert
/// any rows — releasing the empty set is a no-op. Path B (pick-failure)
/// publishes ONE multi-line <c>ReleaseStockV1</c> covering every line that
/// reserved successfully; the saga then waits for the corresponding
/// <c>StockReleasedV1</c> arrivals to drain the counter.</para>
/// </remarks>
public sealed class FulfillmentSagaCompensationTests
{
    private static IReadOnlyList<OrderPlacedLineV1> TwoLines() =>
        new[]
        {
            new OrderPlacedLineV1(OrderLineId: "L1", Sku: "SKU-A", Qty: 2, ExpectedWeight: 100),
            new OrderPlacedLineV1(OrderLineId: "L2", Sku: "SKU-B", Qty: 5, ExpectedWeight: 50),
        };

    private static async Task<ServiceProvider> BuildHarnessAsync()
    {
        var services = new ServiceCollection();
        services.AddLogging(b => b.AddProvider(NullLoggerProvider.Instance));

        // The saga's StockReserved Then handler writes a PickRequestV1 to
        // IPickQueue on its way to AwaitingPick — the unit tests don't
        // read from the queue but the resolution must succeed.
        services.AddSingleton<IPickQueue, PickQueueImpl>();

        services.AddMassTransitTestHarness(cfg =>
        {
            cfg.AddSagaStateMachine<FulfillmentSaga, FulfillmentSagaState>()
                .InMemoryRepository();
        });

        var sp = services.BuildServiceProvider(true);
        await sp.GetRequiredService<ITestHarness>().Start();
        return sp;
    }

    /// <summary>
    /// Drive the saga through OrderPlacedV1 → StockReservedV1 to land it
    /// at AwaitingPick with <c>ReservedLineSkus = "L1,L2"</c> and
    /// <c>LinesAwaitingRelease = 2</c> populated. Mirrors the real flow
    /// the controller would produce — the U5 StockReserved handler is
    /// what writes the line-id set the U7 compensation path depends on.
    /// </summary>
    private static async Task DriveSagaToAwaitingPickAsync(
        ITestHarness harness,
        ISagaStateMachineTestHarness<FulfillmentSaga, FulfillmentSagaState> sagaHarness,
        Guid orderId,
        Guid tenantId
    )
    {
        await harness.Bus.Publish(
            new OrderPlacedV1(
                OrderId: orderId,
                TenantId: tenantId,
                ChannelExternalOrderId: "ext-" + orderId.ToString("N")[..8],
                ShippingProfile: "standard",
                Lines: TwoLines(),
                OccurredAt: DateTime.UtcNow
            )
        );
        (await sagaHarness.Exists(orderId, sagaHarness.StateMachine.AwaitingReservation))
            .Should()
            .NotBeNull();

        await harness.Bus.Publish(
            new StockReservedV1(
                OrderId: orderId,
                TenantId: tenantId,
                LineOutcomes: new[]
                {
                    new LineOutcomeV1("L1", "SKU-A", Guid.NewGuid(), "Reserved"),
                    new LineOutcomeV1("L2", "SKU-B", Guid.NewGuid(), "Reserved"),
                },
                OccurredAt: DateTime.UtcNow
            )
        );
        (await sagaHarness.Exists(orderId, sagaHarness.StateMachine.AwaitingPick))
            .Should()
            .NotBeNull("U5 chains StockReserved → AwaitingPick on the same envelope");
    }

    [Fact]
    public async Task StockReservedV1_PopulatesReservedLineSkusOnSagaState()
    {
        // Pre-check: U7's correctness depends on the U5 StockReserved Then
        // handler populating ReservedLineSkus from the LineOutcomes. Without
        // it the U7 Path B release set would be empty and the saga would
        // jump to Cancelled prematurely. This is the most important U7
        // pre-check (orchestrator brief calls it out explicitly).
        await using var sp = await BuildHarnessAsync();
        var harness = sp.GetRequiredService<ITestHarness>();
        var sagaHarness =
            sp.GetRequiredService<ISagaStateMachineTestHarness<FulfillmentSaga, FulfillmentSagaState>>();

        var orderId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        await DriveSagaToAwaitingPickAsync(harness, sagaHarness, orderId, tenantId);

        var saga = sagaHarness.Created.Contains(orderId);
        saga.Should().NotBeNull();
        saga!.ReservedLineSkus.Should().Contain("L1");
        saga.ReservedLineSkus.Should().Contain("L2");
        saga.LinesAwaitingRelease.Should().Be(2);
    }

    [Fact]
    public async Task PickFailed_PathB_PublishesSingleReleaseStockWithReservedLineIds()
    {
        // Pick-failure happy path (covers F2, AE3 second half in the plan).
        // Saga lands in AwaitingPick with ReservedLineSkus="L1,L2"; PickFailed
        // transitions to CompensatingReservation; the on-enter IfElse else
        // branch publishes ONE ReleaseStockV1 carrying both line ids.
        await using var sp = await BuildHarnessAsync();
        var harness = sp.GetRequiredService<ITestHarness>();
        var sagaHarness =
            sp.GetRequiredService<ISagaStateMachineTestHarness<FulfillmentSaga, FulfillmentSagaState>>();

        var orderId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        await DriveSagaToAwaitingPickAsync(harness, sagaHarness, orderId, tenantId);

        await harness.Bus.Publish(new PickFailed(orderId, "physical stock discrepancy"));

        var compensating = await sagaHarness.Exists(
            orderId,
            sagaHarness.StateMachine.CompensatingReservation
        );
        compensating
            .Should()
            .NotBeNull("PickFailed in AwaitingPick transitions to CompensatingReservation");

        // Exactly one ReleaseStockV1 published for this order with both line ids.
        var released = harness
            .Published.Select<ReleaseStockV1>()
            .Where(p => p.Context.Message.OrderId == orderId)
            .ToList();
        released.Should().HaveCount(1, "the saga publishes one multi-line ReleaseStockV1 per PickFailed");
        var msg = released.Single().Context.Message;
        msg.TenantId.Should().Be(tenantId);
        msg.OrderLineIds.Should().BeEquivalentTo(new[] { "L1", "L2" });
    }

    [Fact]
    public async Task PickFailed_ThenStockReleasedForAllLines_TransitionsToCancelled()
    {
        // Pick failure → ReleaseStockV1 → StockReleasedV1 for both lines
        // → counter 2 → 0 → Cancelled. Mirrors the real production flow
        // where Inventory's ReleaseStockConsumer is the bridge between
        // ReleaseStockV1 and StockReleasedV1.
        await using var sp = await BuildHarnessAsync();
        var harness = sp.GetRequiredService<ITestHarness>();
        var sagaHarness =
            sp.GetRequiredService<ISagaStateMachineTestHarness<FulfillmentSaga, FulfillmentSagaState>>();

        var orderId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        await DriveSagaToAwaitingPickAsync(harness, sagaHarness, orderId, tenantId);

        await harness.Bus.Publish(new PickFailed(orderId, "test"));
        (await sagaHarness.Exists(orderId, sagaHarness.StateMachine.CompensatingReservation))
            .Should()
            .NotBeNull();

        // Inject the StockReleasedV1 the Inventory consumer would emit.
        // The U7 Set-based dedup decrements LinesAwaitingRelease for each
        // fresh line id; once it hits zero the .If guard fires the
        // TransitionTo(Cancelled).
        await harness.Bus.Publish(
            new StockReleasedV1(
                OrderId: orderId,
                TenantId: tenantId,
                OrderLineIds: new[] { "L1", "L2" },
                OccurredAt: DateTime.UtcNow
            )
        );

        var cancelled = await sagaHarness.Exists(
            orderId,
            sagaHarness.StateMachine.Cancelled
        );
        cancelled
            .Should()
            .NotBeNull("once the counter hits 0 the saga transitions to Cancelled");

        var saga = sagaHarness.Created.Contains(orderId)!;
        saga.LinesAwaitingRelease.Should().BeLessOrEqualTo(0);
        saga.ReleasedLineSkus.Should().Contain("L1");
        saga.ReleasedLineSkus.Should().Contain("L2");

        // The Cancelled on-enter activity publishes OrderCancelled so the
        // Outbound-side consumer can flip the Order row.
        (await harness.Published.Any<OrderCancelled>(x => x.Context.Message.OrderId == orderId))
            .Should()
            .BeTrue();
    }

    [Fact]
    public async Task PathA_AtomicReservationFail_ShortCircuitsToCancelledWithoutRelease()
    {
        // Atomic-reservation-fail path: the CTE inserted ZERO rows on the
        // Inventory side; the saga's StockReservationFailed handler sets
        // ReservedLineSkus="" and LinesAwaitingRelease=0. The
        // CompensatingReservation on-enter IfElse branch sees the zero
        // counter and transitions directly to Cancelled — NO ReleaseStockV1
        // publish (release-the-empty-set is a no-op).
        await using var sp = await BuildHarnessAsync();
        var harness = sp.GetRequiredService<ITestHarness>();
        var sagaHarness =
            sp.GetRequiredService<ISagaStateMachineTestHarness<FulfillmentSaga, FulfillmentSagaState>>();

        var orderId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        await harness.Bus.Publish(
            new OrderPlacedV1(
                orderId,
                tenantId,
                "ext-atomic",
                "express",
                TwoLines(),
                DateTime.UtcNow
            )
        );
        (await sagaHarness.Exists(orderId, sagaHarness.StateMachine.AwaitingReservation))
            .Should()
            .NotBeNull();

        await harness.Bus.Publish(
            new StockReservationFailedV1(
                OrderId: orderId,
                TenantId: tenantId,
                LineOutcomes: new[]
                {
                    new LineOutcomeV1("L1", "SKU-A", null, "Reserved"),
                    new LineOutcomeV1("L2", "SKU-B", null, "Oversold"),
                },
                OccurredAt: DateTime.UtcNow
            )
        );

        var cancelled = await sagaHarness.Exists(
            orderId,
            sagaHarness.StateMachine.Cancelled
        );
        cancelled
            .Should()
            .NotBeNull(
                "Path A — atomic-fail saga short-circuits CompensatingReservation → Cancelled"
            );

        // Crucial: no ReleaseStockV1 was published — there's nothing to release.
        var released = harness
            .Published.Select<ReleaseStockV1>()
            .Where(p => p.Context.Message.OrderId == orderId)
            .ToList();
        released.Should().BeEmpty("Path A skips the release publish");

        var saga = sagaHarness.Created.Contains(orderId)!;
        saga.LinesAwaitingRelease.Should().Be(0);
        saga.ReservedLineSkus.Should().BeEmpty();
    }

    [Fact]
    public async Task StockReleased_RedeliveredAfterCancellation_DoesNotDoubleDecrement()
    {
        // Set-based dedup defends the W5 60s p99 compensation gate (U8):
        // MassTransit's at-least-once redelivery must not drive
        // LinesAwaitingRelease negative. Two deliveries of the same
        // StockReleasedV1 — first transitions to Cancelled; second is a
        // no-op (every line id is already in ReleasedLineSkus + Cancelled
        // has no handler for StockReleasedV1 so the redelivery is ignored).
        await using var sp = await BuildHarnessAsync();
        var harness = sp.GetRequiredService<ITestHarness>();
        var sagaHarness =
            sp.GetRequiredService<ISagaStateMachineTestHarness<FulfillmentSaga, FulfillmentSagaState>>();

        var orderId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        await DriveSagaToAwaitingPickAsync(harness, sagaHarness, orderId, tenantId);
        await harness.Bus.Publish(new PickFailed(orderId, "test"));
        (await sagaHarness.Exists(orderId, sagaHarness.StateMachine.CompensatingReservation))
            .Should()
            .NotBeNull();

        // First StockReleasedV1 — drains the counter, transitions to Cancelled.
        await harness.Bus.Publish(
            new StockReleasedV1(orderId, tenantId, new[] { "L1", "L2" }, DateTime.UtcNow)
        );
        (await sagaHarness.Exists(orderId, sagaHarness.StateMachine.Cancelled))
            .Should()
            .NotBeNull();

        var sagaAfterFirst = sagaHarness.Created.Contains(orderId)!;
        var counterAfterFirst = sagaAfterFirst.LinesAwaitingRelease;
        var releasedSkusAfterFirst = sagaAfterFirst.ReleasedLineSkus;

        // Redeliver the same StockReleasedV1. Cancelled has no handler for
        // it; the saga stays put. Even if a redelivery somehow reached the
        // CompensatingReservation handler, the Set-based dedup would
        // recognise both line ids as already-released and skip the decrement.
        await harness.Bus.Publish(
            new StockReleasedV1(orderId, tenantId, new[] { "L1", "L2" }, DateTime.UtcNow)
        );
        await Task.Delay(200); // give MT a beat to apply (or ignore) the redelivery.

        var sagaAfterSecond = sagaHarness.Created.Contains(orderId)!;
        sagaAfterSecond.CurrentState.Should().Be("Cancelled");
        sagaAfterSecond
            .LinesAwaitingRelease.Should()
            .Be(counterAfterFirst, "the redelivery must not decrement the counter further");
        sagaAfterSecond
            .ReleasedLineSkus.Should()
            .Be(releasedSkusAfterFirst, "the dedup set is stable across redelivery");

        // Exactly one OrderCancelled — the saga's Cancelled on-enter
        // activity fires exactly once.
        var orderCancelled = harness
            .Published.Select<OrderCancelled>()
            .Where(p => p.Context.Message.OrderId == orderId)
            .ToList();
        orderCancelled
            .Should()
            .HaveCount(1, "Cancelled is terminal; OrderCancelled publishes exactly once");
    }

    [Fact]
    public async Task StockReleased_PartialDelivery_StaysInCompensatingUntilAllArrive()
    {
        // Saga in CompensatingReservation with LinesAwaitingRelease=2.
        // First StockReleasedV1 covers only L1: counter 2 → 1, saga stays
        // in CompensatingReservation. Second StockReleasedV1 covers L2:
        // counter 1 → 0, transitions to Cancelled. Exercises the
        // partial-set release path (Inventory's ReleaseLinesAsync filters
        // by status='Pending' so a flaky middle-of-batch consumer can emit
        // its results in tranches).
        await using var sp = await BuildHarnessAsync();
        var harness = sp.GetRequiredService<ITestHarness>();
        var sagaHarness =
            sp.GetRequiredService<ISagaStateMachineTestHarness<FulfillmentSaga, FulfillmentSagaState>>();

        var orderId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        await DriveSagaToAwaitingPickAsync(harness, sagaHarness, orderId, tenantId);
        await harness.Bus.Publish(new PickFailed(orderId, "test"));
        (await sagaHarness.Exists(orderId, sagaHarness.StateMachine.CompensatingReservation))
            .Should()
            .NotBeNull();

        await harness.Bus.Publish(
            new StockReleasedV1(orderId, tenantId, new[] { "L1" }, DateTime.UtcNow)
        );
        // Brief settle so the Then handler runs.
        await Task.Delay(200);

        var afterFirst = sagaHarness.Created.Contains(orderId)!;
        afterFirst
            .CurrentState.Should()
            .Be("CompensatingReservation", "only 1/2 lines released — counter at 1");
        afterFirst.LinesAwaitingRelease.Should().Be(1);
        afterFirst.ReleasedLineSkus.Should().Contain("L1");
        afterFirst.ReleasedLineSkus.Should().NotContain("L2");

        await harness.Bus.Publish(
            new StockReleasedV1(orderId, tenantId, new[] { "L2" }, DateTime.UtcNow)
        );
        (await sagaHarness.Exists(orderId, sagaHarness.StateMachine.Cancelled))
            .Should()
            .NotBeNull("both lines now released — counter at 0 → Cancelled");

        var finalSaga = sagaHarness.Created.Contains(orderId)!;
        finalSaga.ReleasedLineSkus.Should().Contain("L1");
        finalSaga.ReleasedLineSkus.Should().Contain("L2");
    }

    // ── Sprint-12.5 U3 — Path C: ShipFailed compensation ────────────────────
    //
    // Saga state Packed + ShipFailed → CompensatingReservation. Reuses the
    // Path B compensation primitives (LinesAwaitingRelease counter +
    // ReservedLineSkus set + WhenEnter IfElse activity) unchanged. The
    // counter survives through AwaitingPick → Picked → Packed because no
    // transition handler clears it.

    /// <summary>
    /// Drive the saga from Initial through AwaitingPick → Picked → Packed.
    /// Mirrors <see cref="DriveSagaToAwaitingPickAsync"/> but extends to
    /// Packed state needed for Path C entry.
    /// </summary>
    private static async Task DriveSagaToPackedAsync(
        ITestHarness harness,
        ISagaStateMachineTestHarness<FulfillmentSaga, FulfillmentSagaState> sagaHarness,
        Guid orderId,
        Guid tenantId
    )
    {
        await DriveSagaToAwaitingPickAsync(harness, sagaHarness, orderId, tenantId);

        await harness.Bus.Publish(new PickConfirmed(orderId));
        (await sagaHarness.Exists(orderId, sagaHarness.StateMachine.Picked))
            .Should()
            .NotBeNull("PickConfirmed in AwaitingPick transitions to Picked");

        await harness.Bus.Publish(new PackConfirmed(orderId, ActualWeightTotal: 250));
        (await sagaHarness.Exists(orderId, sagaHarness.StateMachine.Packed))
            .Should()
            .NotBeNull("PackConfirmed in Picked transitions to Packed");
    }

    [Fact]
    public async Task ShipFailed_PathC_PublishesSingleReleaseStockWithReservedLineIds()
    {
        // Path C happy path. Saga drives to Packed with ReservedLineSkus="L1,L2"
        // + LinesAwaitingRelease=2 (set at AwaitingReservation → Reserved,
        // unchanged through to Packed). ShipFailed transitions to
        // CompensatingReservation; the WhenEnter IfElse Else-branch publishes
        // ONE ReleaseStockV1 carrying both line ids — exactly Path B's behavior.
        await using var sp = await BuildHarnessAsync();
        var harness = sp.GetRequiredService<ITestHarness>();
        var sagaHarness =
            sp.GetRequiredService<ISagaStateMachineTestHarness<FulfillmentSaga, FulfillmentSagaState>>();

        var orderId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        await DriveSagaToPackedAsync(harness, sagaHarness, orderId, tenantId);

        // Before ShipFailed: counter + line set populated unchanged from Reserved.
        var beforeShipFail = sagaHarness.Created.Contains(orderId)!;
        beforeShipFail.LinesAwaitingRelease.Should().Be(2);
        beforeShipFail.ReservedLineSkus.Should().Contain("L1");
        beforeShipFail.ReservedLineSkus.Should().Contain("L2");

        await harness.Bus.Publish(new ShipFailed(orderId, "carrier rejected label"));

        var compensating = await sagaHarness.Exists(
            orderId,
            sagaHarness.StateMachine.CompensatingReservation
        );
        compensating
            .Should()
            .NotBeNull("ShipFailed in Packed transitions to CompensatingReservation");

        var released = harness
            .Published.Select<ReleaseStockV1>()
            .Where(p => p.Context.Message.OrderId == orderId)
            .ToList();
        released
            .Should()
            .HaveCount(1, "Path C publishes one multi-line ReleaseStockV1, same as Path B");
        var msg = released.Single().Context.Message;
        msg.TenantId.Should().Be(tenantId);
        msg.OrderLineIds.Should().BeEquivalentTo(new[] { "L1", "L2" });
    }

    [Fact]
    public async Task ShipFailed_ThenStockReleasedForAllLines_TransitionsToCancelled()
    {
        // Path C full flow: ShipFailed → ReleaseStockV1 → StockReleasedV1
        // for both lines → counter 2 → 0 → Cancelled. Identical to Path B's
        // counter-drain behavior — Sprint-12.5 KTD5 reuse claim under test.
        await using var sp = await BuildHarnessAsync();
        var harness = sp.GetRequiredService<ITestHarness>();
        var sagaHarness =
            sp.GetRequiredService<ISagaStateMachineTestHarness<FulfillmentSaga, FulfillmentSagaState>>();

        var orderId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        await DriveSagaToPackedAsync(harness, sagaHarness, orderId, tenantId);

        await harness.Bus.Publish(new ShipFailed(orderId, "damaged in loading"));
        (await sagaHarness.Exists(orderId, sagaHarness.StateMachine.CompensatingReservation))
            .Should()
            .NotBeNull();

        await harness.Bus.Publish(
            new StockReleasedV1(
                OrderId: orderId,
                TenantId: tenantId,
                OrderLineIds: new[] { "L1", "L2" },
                OccurredAt: DateTime.UtcNow
            )
        );

        var cancelled = await sagaHarness.Exists(orderId, sagaHarness.StateMachine.Cancelled);
        cancelled
            .Should()
            .NotBeNull("Path C drains identically to Path B once StockReleased arrives");

        (await harness.Published.Any<OrderCancelled>(x => x.Context.Message.OrderId == orderId))
            .Should()
            .BeTrue("Cancelled on-enter publishes OrderCancelled — Path C parity with Path B");
    }

    [Fact]
    public async Task ShipFailed_InWrongState_IsIgnoredAsOutOfBand()
    {
        // Saga still in AwaitingPick (no PackConfirmed yet). ShipFailed has
        // no When mapping in AwaitingPick — MT treats it as out-of-band and
        // the saga stays put. Defends against a controller-side race where
        // the operator hits mark-ship-failed before the saga has progressed
        // past Picked.
        await using var sp = await BuildHarnessAsync();
        var harness = sp.GetRequiredService<ITestHarness>();
        var sagaHarness =
            sp.GetRequiredService<ISagaStateMachineTestHarness<FulfillmentSaga, FulfillmentSagaState>>();

        var orderId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        await DriveSagaToAwaitingPickAsync(harness, sagaHarness, orderId, tenantId);

        await harness.Bus.Publish(new ShipFailed(orderId, "premature"));
        await Task.Delay(200);

        var saga = sagaHarness.Created.Contains(orderId)!;
        saga.CurrentState.Should().Be("AwaitingPick", "saga ignores ShipFailed outside Packed state");

        var released = harness
            .Published.Select<ReleaseStockV1>()
            .Where(p => p.Context.Message.OrderId == orderId)
            .ToList();
        released.Should().BeEmpty();
    }

    [Fact]
    public async Task PickFailed_InWrongState_IsIgnoredAsOutOfBand()
    {
        // Saga still in AwaitingReservation (StockReserved hasn't landed
        // yet). PickFailed has no When mapping in AwaitingReservation, so
        // MT treats it as out-of-band and the saga stays put. Defends
        // against a controller-side race where the operator hits
        // mark-pick-failed before the saga has progressed past
        // AwaitingReservation.
        await using var sp = await BuildHarnessAsync();
        var harness = sp.GetRequiredService<ITestHarness>();
        var sagaHarness =
            sp.GetRequiredService<ISagaStateMachineTestHarness<FulfillmentSaga, FulfillmentSagaState>>();

        var orderId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        await harness.Bus.Publish(
            new OrderPlacedV1(
                orderId,
                tenantId,
                "ext-oob",
                "standard",
                TwoLines(),
                DateTime.UtcNow
            )
        );
        (await sagaHarness.Exists(orderId, sagaHarness.StateMachine.AwaitingReservation))
            .Should()
            .NotBeNull();

        await harness.Bus.Publish(new PickFailed(orderId, "premature"));
        await Task.Delay(200);

        var saga = sagaHarness.Created.Contains(orderId)!;
        saga.CurrentState.Should().Be("AwaitingReservation");

        // No ReleaseStockV1 published — the saga didn't transition into
        // CompensatingReservation so the on-enter activity never fired.
        var released = harness
            .Published.Select<ReleaseStockV1>()
            .Where(p => p.Context.Message.OrderId == orderId)
            .ToList();
        released.Should().BeEmpty();
    }
}
