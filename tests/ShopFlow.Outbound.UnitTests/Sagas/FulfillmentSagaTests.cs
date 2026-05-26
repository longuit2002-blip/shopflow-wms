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
/// Sprint-3-redux U4 — <see cref="FulfillmentSaga"/> happy-path + edge-case
/// transitions driven through MassTransit's <see cref="ITestHarness"/>
/// (the v8.x replacement for the deprecated <c>InMemoryTestHarness</c>).
/// </summary>
/// <remarks>
/// <para>This unit test exercises the state machine in isolation against
/// MassTransit's in-memory saga repository — NO Postgres + NO EF saga repo;
/// see <c>tests/ShopFlow.Outbound.IntegrationTests/FulfillmentSagaPersistenceTests.cs</c>
/// for the EF persistence path + <c>SagaPerTenantBindingTests.cs</c> for the
/// K12 per-tenant DbContext binding gate.</para>
///
/// <para>Per the U4 execution note, these tests were written FIRST: the test
/// file lands, fails with "FulfillmentSaga not defined", then the state
/// machine is implemented to make them pass. Catches MassTransit DSL
/// misunderstandings (Initially/When/Then/TransitionTo) before they spread.</para>
///
/// <para>The Reserved → AwaitingPick auto-transition is intentionally
/// covered up to <c>Reserved</c> only; U5 wires the <c>IPickQueue</c>
/// write that drives the auto-transition forward. The PickConfirmed /
/// PackConfirmed / ShipConfirmed in-process events compile here so the
/// saga structure is complete; U6 wires the controllers that publish them.</para>
/// </remarks>
public sealed class FulfillmentSagaTests
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

        // U5 — IPickQueue Singleton: the StockReserved Then handler
        // resolves IPickQueue via GetPayload<IServiceProvider>() and
        // writes a PickRequestV1 envelope on the way to AwaitingPick.
        // The unit tests don't read the channel, but the resolution
        // must succeed so the saga commit completes.
        services.AddSingleton<IPickQueue, PickQueueImpl>();

        services.AddMassTransitTestHarness(cfg =>
        {
            cfg.AddSagaStateMachine<FulfillmentSaga, FulfillmentSagaState>().InMemoryRepository();
        });

        var sp = services.BuildServiceProvider(true);
        await sp.GetRequiredService<ITestHarness>().Start();
        return sp;
    }

    [Fact]
    public async Task OrderPlacedV1_TransitionsSagaToAwaitingReservation_AndPublishesReserveStock()
    {
        await using var sp = await BuildHarnessAsync();
        var harness = sp.GetRequiredService<ITestHarness>();
        var sagaHarness = sp.GetRequiredService<
            ISagaStateMachineTestHarness<FulfillmentSaga, FulfillmentSagaState>
        >();

        var orderId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var msg = new OrderPlacedV1(
            OrderId: orderId,
            TenantId: tenantId,
            ChannelExternalOrderId: "ext-saga-1",
            ShippingProfile: "standard",
            Lines: TwoLines(),
            OccurredAt: DateTime.UtcNow
        );

        await harness.Bus.Publish(msg);

        (await harness.Consumed.Any<OrderPlacedV1>()).Should().BeTrue();

        // Exists() awaits until the saga reaches the requested state — the
        // synchronous Created.ContainsInState() doesn't wait, so use the
        // async overload for state assertions.
        var inAwaitingReservation = await sagaHarness.Exists(
            orderId,
            sagaHarness.StateMachine.AwaitingReservation
        );
        inAwaitingReservation
            .Should()
            .NotBeNull("the saga should have transitioned to AwaitingReservation on OrderPlacedV1");

        // The saga must publish ReserveStockV1 carrying the same OrderId.
        (await harness.Published.Any<ReserveStockV1>(x => x.Context.Message.OrderId == orderId))
            .Should()
            .BeTrue();

        // ShippingProfile + LineCount captured on the saga state for U5/U7.
        var sagaInstance = sagaHarness.Created.Contains(orderId);
        sagaInstance.Should().NotBeNull();
        sagaInstance!.ShippingProfile.Should().Be("standard");
        sagaInstance.LineCount.Should().Be(2);
        sagaInstance.TenantId.Should().Be(tenantId);
    }

    [Fact]
    public async Task StockReservedV1_FromAwaitingReservation_TransitionsThroughReservedToAwaitingPick()
    {
        // U5 — the StockReserved Then handler writes a PickRequestV1 to
        // IPickQueue and chains TransitionTo(AwaitingPick), so the saga
        // flows AwaitingReservation → Reserved → AwaitingPick on the
        // same envelope. The final state is AwaitingPick; Reserved is
        // transient.
        await using var sp = await BuildHarnessAsync();
        var harness = sp.GetRequiredService<ITestHarness>();
        var sagaHarness = sp.GetRequiredService<
            ISagaStateMachineTestHarness<FulfillmentSaga, FulfillmentSagaState>
        >();
        var queue = sp.GetRequiredService<IPickQueue>();

        var orderId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        await harness.Bus.Publish(
            new OrderPlacedV1(orderId, tenantId, "ext-r1", "standard", TwoLines(), DateTime.UtcNow)
        );
        (await sagaHarness.Exists(orderId, sagaHarness.StateMachine.AwaitingReservation))
            .Should()
            .NotBeNull();

        var reserved = new StockReservedV1(
            OrderId: orderId,
            TenantId: tenantId,
            LineOutcomes: new[]
            {
                new LineOutcomeV1("L1", "SKU-A", Guid.NewGuid(), "Reserved"),
                new LineOutcomeV1("L2", "SKU-B", Guid.NewGuid(), "Reserved"),
            },
            OccurredAt: DateTime.UtcNow
        );
        await harness.Bus.Publish(reserved);

        var inAwaitingPick = await sagaHarness.Exists(
            orderId,
            sagaHarness.StateMachine.AwaitingPick
        );
        inAwaitingPick
            .Should()
            .NotBeNull(
                "U5 chains StockReserved → Reserved → AwaitingPick in the same Then handler"
            );

        // The saga state captures the reserved line ids for U7 compensation.
        var sagaInstance = sagaHarness.Created.Contains(orderId)!;
        sagaInstance.ReservedLineSkus.Should().Contain("L1");
        sagaInstance.ReservedLineSkus.Should().Contain("L2");

        // U5 — the PickRequestV1 envelope landed on the tenant's queue.
        var reader = queue.GetReader(tenantId);
        reader
            .TryRead(out var item)
            .Should()
            .BeTrue("the saga must write a PickRequestV1 to the queue");
        item!.OrderId.Should().Be(orderId);
        item.TenantId.Should().Be(tenantId);
        item.ShippingProfile.Should().Be("standard");
        item.LineCount.Should().Be(2);
    }

    [Fact]
    public async Task StockReservationFailedV1_FromAwaitingReservation_ShortCircuitsThroughCompensatingReservationToCancelled()
    {
        // U7 Path A — atomic-CTE failure inserted 0 ledger rows, so
        // ReservedLineSkus = "" and LinesAwaitingRelease = 0. The
        // CompensatingReservation on-enter activity fires
        // TransitionTo(Cancelled) immediately on the IfElse Then-branch
        // ("release the empty set" is a no-op). The saga therefore races
        // through CompensatingReservation in the same commit and lands at
        // Cancelled. Pre-U7 this test asserted CompensatingReservation as
        // the final state; U7's correct behavior is the short-circuit
        // because the saga has nothing to wait for.
        await using var sp = await BuildHarnessAsync();
        var harness = sp.GetRequiredService<ITestHarness>();
        var sagaHarness = sp.GetRequiredService<
            ISagaStateMachineTestHarness<FulfillmentSaga, FulfillmentSagaState>
        >();

        var orderId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        await harness.Bus.Publish(
            new OrderPlacedV1(orderId, tenantId, "ext-f1", "express", TwoLines(), DateTime.UtcNow)
        );
        (await sagaHarness.Exists(orderId, sagaHarness.StateMachine.AwaitingReservation))
            .Should()
            .NotBeNull();

        var failed = new StockReservationFailedV1(
            OrderId: orderId,
            TenantId: tenantId,
            LineOutcomes: new[]
            {
                new LineOutcomeV1("L1", "SKU-A", null, "Reserved"),
                new LineOutcomeV1("L2", "SKU-B", null, "Oversold"),
            },
            OccurredAt: DateTime.UtcNow
        );
        await harness.Bus.Publish(failed);

        var cancelled = await sagaHarness.Exists(orderId, sagaHarness.StateMachine.Cancelled);
        cancelled
            .Should()
            .NotBeNull(
                "U7 Path A short-circuits CompensatingReservation → Cancelled when there is nothing to release"
            );
    }

    [Fact]
    public async Task OrderPlacedV1_BindsCorrelationIdToOrderId_AndSetsCurrentState()
    {
        await using var sp = await BuildHarnessAsync();
        var harness = sp.GetRequiredService<ITestHarness>();
        var sagaHarness = sp.GetRequiredService<
            ISagaStateMachineTestHarness<FulfillmentSaga, FulfillmentSagaState>
        >();

        var orderId = Guid.NewGuid();
        await harness.Bus.Publish(
            new OrderPlacedV1(
                OrderId: orderId,
                TenantId: Guid.NewGuid(),
                ChannelExternalOrderId: "ext-corr",
                ShippingProfile: "standard",
                Lines: TwoLines(),
                OccurredAt: DateTime.UtcNow
            )
        );

        // The saga awaits via the async Exists API: K2's CorrelateById on
        // OrderId places the saga state under PK = OrderId, and the string
        // CurrentState column matches the named state after the transition.
        (await sagaHarness.Exists(orderId, sagaHarness.StateMachine.AwaitingReservation))
            .Should()
            .NotBeNull();

        var inst = sagaHarness.Created.Contains(orderId);
        inst.Should().NotBeNull();
        inst!.CorrelationId.Should().Be(orderId);
        inst.CurrentState.Should().Be("AwaitingReservation");
    }

    [Fact]
    public async Task PackConfirmed_InAwaitingReservation_IsIgnoredAsOutOfBand()
    {
        // Defensive: a stray PackConfirmed event for a saga that's still in
        // AwaitingReservation should not transition (no When mapping for
        // PackConfirmed in that state) — MassTransit ignores it by default
        // and the saga stays put.
        await using var sp = await BuildHarnessAsync();
        var harness = sp.GetRequiredService<ITestHarness>();
        var sagaHarness = sp.GetRequiredService<
            ISagaStateMachineTestHarness<FulfillmentSaga, FulfillmentSagaState>
        >();

        var orderId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        await harness.Bus.Publish(
            new OrderPlacedV1(orderId, tenantId, "ext-ob", "standard", TwoLines(), DateTime.UtcNow)
        );
        (await sagaHarness.Exists(orderId, sagaHarness.StateMachine.AwaitingReservation))
            .Should()
            .NotBeNull();

        await harness.Bus.Publish(new PackConfirmed(orderId, ActualWeightTotal: 250));

        // Give MT a brief moment to process (or reject) the out-of-band event.
        await Task.Delay(200);

        // Saga should still be in AwaitingReservation — PackConfirmed has no
        // mapping in that state, so MassTransit treats it as out-of-band and
        // the state stays put.
        var still = await sagaHarness.Exists(orderId, sagaHarness.StateMachine.AwaitingReservation);
        still.Should().NotBeNull("PackConfirmed in AwaitingReservation is out-of-band");

        // Defensive: also assert the saga did NOT transition to Packed.
        var inPacked = sagaHarness.Created.Contains(orderId);
        inPacked.Should().NotBeNull();
        inPacked!.CurrentState.Should().NotBe("Packed");
    }
}
