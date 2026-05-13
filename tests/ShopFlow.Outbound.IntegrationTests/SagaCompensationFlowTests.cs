using MassTransit;
using MassTransit.Testing;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using ShopFlow.Contracts.Inventory;
using ShopFlow.Contracts.Outbound;
using ShopFlow.Outbound.Api.Contracts;
using ShopFlow.Outbound.Api.Controllers;
using ShopFlow.Outbound.Application.Ports;
using ShopFlow.Outbound.Application.Sagas;
using ShopFlow.Outbound.Domain;
using ShopFlow.Outbound.Infrastructure;
using ShopFlow.Outbound.Infrastructure.Consumers;
using ShopFlow.Outbound.Infrastructure.Outbox;
using ShopFlow.Outbound.Infrastructure.Repositories;
using ShopFlow.SharedKernel.Application;

namespace ShopFlow.Outbound.IntegrationTests;

/// <summary>
/// Sprint-3-redux U9 — saga compensation flow integration test. Drives one
/// order from Created through to the terminal Cancelled state via the
/// pick-failure compensation path (AwaitingPick → CompensatingReservation →
/// Cancelled) against a real Postgres tenant DB + an in-memory MassTransit
/// harness running the <see cref="FulfillmentSaga"/> with its EF saga
/// repository AND the <see cref="OrderCancelledConsumer"/> bridge.
/// </summary>
/// <remarks>
/// <para>Complement to <see cref="SagaHappyPathTests"/>. Where the happy
/// path verifies Created → Shipped, this test verifies Created →
/// CompensatingReservation → Cancelled — the operator pulls the
/// pick-failure trigger after the saga reached AwaitingPick. The saga
/// publishes <see cref="ReleaseStockV1"/>; the test simulates the
/// Inventory side by re-publishing <see cref="StockReleasedV1"/> with all
/// line ids; the saga's Set-based dedup drains the counter to zero and
/// transitions to Cancelled; the <see cref="OrderCancelledConsumer"/>
/// flips the Order row.</para>
///
/// <para>Differs from <see cref="PickFailureCompensationTests"/>: that
/// test scope is the saga ⇄ Order R3 loop with several edge-case
/// variants (wrong state, duplicate POST); this U9 test is the
/// canonical end-to-end PR-time gate that the full compensation flow
/// reaches the Cancelled terminal state cleanly.</para>
/// </remarks>
[Collection(OutboundTenantCollection.Name)]
[Trait("Category", "Integration")]
public sealed class SagaCompensationFlowTests : IAsyncLifetime
{
    private static readonly DateTimeOffset FixedNow =
        new(2026, 5, 13, 10, 0, 0, TimeSpan.Zero);

    private readonly OutboundTenantFixture _fx;
    private ProvisionedOutboundTenant _tenant = default!;

    public SagaCompensationFlowTests(OutboundTenantFixture fx)
    {
        _fx = fx;
    }

    public async Task InitializeAsync()
    {
        _tenant = await _fx.ProvisionTenantAsync("saga-comp");
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task FullSaga_CreatedToCancelled_ViaPickFailureCompensation_EndsAtCancelled()
    {
        await using var sp = await BuildHostAsync();
        var harness = sp.GetRequiredService<ITestHarness>();

        // 1. POST /orders → Order in Created state.
        var orderId = await CreateOrderViaControllerAsync("ext-comp-1");

        // 2. Publish OrderPlacedV1 → saga to AwaitingReservation +
        //    publishes ReserveStockV1.
        await harness.Bus.Publish(
            new OrderPlacedV1(
                OrderId: orderId,
                TenantId: _tenant.Info.Id,
                ChannelExternalOrderId: "ext-comp-1",
                ShippingProfile: "standard",
                Lines: new[]
                {
                    new OrderPlacedLineV1("L1", "SKU-A", 1, 100),
                    new OrderPlacedLineV1("L2", "SKU-B", 1, 50),
                },
                OccurredAt: DateTime.UtcNow
            )
        );
        await WaitForSagaStateAsync(orderId, "AwaitingReservation");

        // 3. Stub Inventory: emit StockReservedV1 with both lines reserved
        //    → saga to AwaitingPick.
        await harness.Bus.Publish(
            new StockReservedV1(
                OrderId: orderId,
                TenantId: _tenant.Info.Id,
                LineOutcomes: new[]
                {
                    new LineOutcomeV1("L1", "SKU-A", Guid.NewGuid(), "Reserved"),
                    new LineOutcomeV1("L2", "SKU-B", Guid.NewGuid(), "Reserved"),
                },
                OccurredAt: DateTime.UtcNow
            )
        );
        await WaitForSagaStateAsync(orderId, "AwaitingPick");

        // 4. Walk the Order aggregate forward in lockstep so the
        //    /mark-pick-failed controller endpoint's pre-state guard passes.
        await SetOrderStatusAsync(orderId, OrderStatus.AwaitingPick);

        // 5. POST /mark-pick-failed — Order to CompensatingReservation;
        //    PickFailed event publishes; saga AwaitingPick →
        //    CompensatingReservation; the WhenEnter activity publishes
        //    ReleaseStockV1 (Path B, LinesAwaitingRelease=2).
        await using var scope = sp.CreateAsyncScope();
        var publishEndpoint = scope.ServiceProvider.GetRequiredService<IPublishEndpoint>();
        await using (var ctrlHarness = BuildControllerHarness(publishEndpoint))
        {
            var result = await ctrlHarness.Controller.MarkPickFailedAsync(
                orderId,
                new MarkPickFailedRequest("physical discrepancy"),
                CancellationToken.None
            );
            result.Should().BeOfType<OkObjectResult>();
        }
        await WaitForSagaStateAsync(orderId, "CompensatingReservation");

        // 6. Saga published ReleaseStockV1 with both line ids.
        var released = harness
            .Published.Select<ReleaseStockV1>()
            .Where(p => p.Context.Message.OrderId == orderId)
            .ToList();
        released.Should().HaveCount(1, "Path B publishes ONE multi-line ReleaseStockV1");
        released
            .Single()
            .Context.Message.OrderLineIds.Should()
            .BeEquivalentTo(new[] { "L1", "L2" });

        // 7. Stub Inventory: emit StockReleasedV1 with both line ids →
        //    saga's Set-based dedup drains LinesAwaitingRelease to zero →
        //    transitions to Cancelled → publishes OrderCancelled →
        //    OrderCancelledConsumer flips Order row to Cancelled.
        await harness.Bus.Publish(
            new StockReleasedV1(
                OrderId: orderId,
                TenantId: _tenant.Info.Id,
                OrderLineIds: new[] { "L1", "L2" },
                OccurredAt: DateTime.UtcNow
            )
        );

        await WaitForSagaStateAsync(orderId, "Cancelled");
        await WaitForOrderStatusAsync(orderId, OrderStatus.Cancelled);

        // ── Assertions ──────────────────────────────────────────────────
        await using var verify = new OutboundDbContext(_tenant.Options);
        var order = await verify.Orders.AsNoTracking().SingleAsync(o => o.Id == orderId);
        order.Status.Should().Be(OrderStatus.Cancelled);
        order.LabelUrl.Should().BeNull("compensation path never reaches the carrier");
        order.TrackingNumber.Should().BeNull();
    }

    [Fact]
    public async Task ReservationFailedAtomicCte_FastTracksFromAwaitingReservationToCancelled()
    {
        // Plan U9 Path A: AwaitingReservation + StockReservationFailedV1 →
        // CompensatingReservation (no release publish, empty release-set)
        // → Cancelled. Verifies the "release-the-empty-set" short-circuit
        // in FulfillmentSaga.WhenEnter(CompensatingReservation).
        await using var sp = await BuildHostAsync();
        var harness = sp.GetRequiredService<ITestHarness>();

        var orderId = await CreateOrderViaControllerAsync("ext-comp-pathA");

        await harness.Bus.Publish(
            new OrderPlacedV1(
                OrderId: orderId,
                TenantId: _tenant.Info.Id,
                ChannelExternalOrderId: "ext-comp-pathA",
                ShippingProfile: "standard",
                Lines: new[]
                {
                    new OrderPlacedLineV1("L1", "SKU-A", 999, 100),
                },
                OccurredAt: DateTime.UtcNow
            )
        );
        await WaitForSagaStateAsync(orderId, "AwaitingReservation");

        // Inventory side: emit StockReservationFailedV1 (the atomic-CTE
        // failure case — no ledger rows inserted, no lines to release).
        await harness.Bus.Publish(
            new StockReservationFailedV1(
                OrderId: orderId,
                TenantId: _tenant.Info.Id,
                LineOutcomes: new[]
                {
                    new LineOutcomeV1("L1", "SKU-A", null, "Oversold"),
                },
                OccurredAt: DateTime.UtcNow
            )
        );

        // Saga transitions AwaitingReservation → CompensatingReservation →
        // (no release publish, LinesAwaitingRelease==0) → Cancelled in one
        // dispatch tick.
        await WaitForSagaStateAsync(orderId, "Cancelled");

        // No ReleaseStockV1 was published — Path A is the empty-release.
        var released = harness
            .Published.Select<ReleaseStockV1>()
            .Where(p => p.Context.Message.OrderId == orderId)
            .ToList();
        released.Should().BeEmpty(
            "Path A (atomic CTE failure) must NOT publish ReleaseStockV1 — there's nothing to release."
        );

        // OrderCancelled consumer flips Order to Cancelled (Order was
        // still in Created — MarkCancelled accepts Created → Cancelled).
        await WaitForOrderStatusAsync(orderId, OrderStatus.Cancelled);
    }

    // ── Harness wiring ────────────────────────────────────────────────────

    private async Task<ServiceProvider> BuildHostAsync()
    {
        var services = new ServiceCollection();
        services.AddLogging(b => b.AddProvider(NullLoggerProvider.Instance));

        var rc = _tenant.BuildRequestContext();
        services.AddSingleton<RequestContext>(rc);
        services.AddSingleton<IRequestContext>(rc);
        services.AddSingleton<TimeProvider>(new FakeClock(FixedNow));

        services.AddScoped<OutboundDbContext>(_ => new OutboundDbContext(_tenant.Options));
        services.AddScoped<IOrderRepository, OrderRepository>();
        services.AddScoped<IUnitOfWork, OutboundUnitOfWork>();
        services.AddScoped<IOutboundOutbox, OutboundOutbox>();
        services.AddSingleton<IPickQueue, ShopFlow.Outbound.Infrastructure.PickQueue.PickQueue>();

        services.AddMassTransitTestHarness(cfg =>
        {
            cfg.AddSagaStateMachine<FulfillmentSaga, FulfillmentSagaState>()
                .EntityFrameworkRepository(r =>
                {
                    r.ExistingDbContext<OutboundDbContext>();
                    r.UsePostgres();
                });
            cfg.AddConsumer<OrderCancelledConsumer>();
        });

        var sp = services.BuildServiceProvider(true);
        await sp.GetRequiredService<ITestHarness>().Start();
        return sp;
    }

    private async Task<Guid> CreateOrderViaControllerAsync(string externalId)
    {
        await using var harness = BuildControllerHarness(new NoopPublishEndpoint());
        var result = await harness.Controller.CreateAsync(
            new CreateOrderRequest(
                ChannelExternalOrderId: externalId,
                ShippingProfile: "standard",
                Lines: new[]
                {
                    new CreateOrderLineRequest("SKU-A", 1, 100),
                    new CreateOrderLineRequest("SKU-B", 1, 50),
                }
            ),
            CancellationToken.None
        );
        var created = result.Should().BeOfType<CreatedAtActionResult>().Subject;
        return ((OrderResponse)created.Value!).Id;
    }

    private ControllerHarness BuildControllerHarness(IPublishEndpoint publishEndpoint)
    {
        var db = new OutboundDbContext(_tenant.Options);
        var rc = _tenant.BuildRequestContext();
        var clock = new FakeClock(FixedNow);
        var outbox = new OutboundOutbox(db, rc, clock);

        var controller = new OrdersController(
            orderRepo: new OrderRepository(db),
            uow: new OutboundUnitOfWork(db),
            outbox: outbox,
            requestContext: rc,
            clock: clock,
            publishEndpoint: publishEndpoint,
            shippingProvider: new UnusedMockShippingProvider()
        );
        return new ControllerHarness(controller, db);
    }

    private async Task SetOrderStatusAsync(Guid orderId, OrderStatus status)
    {
        await using var conn = new NpgsqlConnection(_tenant.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE orders SET status = @status WHERE id = @id";
        cmd.Parameters.AddWithValue("status", status.ToString());
        cmd.Parameters.AddWithValue("id", orderId);
        var rows = await cmd.ExecuteNonQueryAsync();
        rows.Should().Be(1);
    }

    private async Task WaitForSagaStateAsync(Guid orderId, string expectedState)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (DateTime.UtcNow < deadline)
        {
            await using var conn = new NpgsqlConnection(_tenant.ConnectionString);
            await conn.OpenAsync();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText =
                """SELECT "CurrentState" FROM saga_state WHERE "CorrelationId" = @oid""";
            cmd.Parameters.AddWithValue("oid", orderId);
            var state = (string?)await cmd.ExecuteScalarAsync();
            if (state == expectedState)
            {
                return;
            }
            await Task.Delay(50);
        }
        throw new TimeoutException(
            $"Saga {orderId} did not reach state {expectedState} within 10s."
        );
    }

    private async Task WaitForOrderStatusAsync(Guid orderId, OrderStatus expected)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (DateTime.UtcNow < deadline)
        {
            await using var verify = new OutboundDbContext(_tenant.Options);
            var order = await verify.Orders.AsNoTracking().SingleAsync(o => o.Id == orderId);
            if (order.Status == expected)
            {
                return;
            }
            await Task.Delay(50);
        }
        throw new TimeoutException(
            $"Order {orderId} did not reach status {expected} within 10s."
        );
    }

    private sealed record ControllerHarness(OrdersController Controller, OutboundDbContext Db)
        : IAsyncDisposable
    {
        public async ValueTask DisposeAsync() => await Db.DisposeAsync();
    }

    private sealed class FakeClock : TimeProvider
    {
        private readonly DateTimeOffset _now;

        public FakeClock(DateTimeOffset now) => _now = now;

        public override DateTimeOffset GetUtcNow() => _now;
    }

    /// <summary>
    /// Mock that throws if reached — the compensation path never calls
    /// the carrier, so any invocation here is a test-wiring bug.
    /// </summary>
    private sealed class UnusedMockShippingProvider : IMockShippingProvider
    {
        public Task<ShippingLabel> CreateLabelAsync(Order order, CancellationToken ct) =>
            throw new InvalidOperationException(
                "SagaCompensationFlowTests should never reach the shipping path."
            );
    }

    private sealed class NoopPublishEndpoint : IPublishEndpoint
    {
        public Task Publish<T>(T message, CancellationToken cancellationToken = default)
            where T : class => Task.CompletedTask;

        public Task Publish<T>(
            T message,
            IPipe<PublishContext<T>> publishPipe,
            CancellationToken cancellationToken = default
        )
            where T : class => Task.CompletedTask;

        public Task Publish<T>(
            T message,
            IPipe<PublishContext> publishPipe,
            CancellationToken cancellationToken = default
        )
            where T : class => Task.CompletedTask;

        public Task Publish(object message, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task Publish(
            object message,
            IPipe<PublishContext> publishPipe,
            CancellationToken cancellationToken = default
        ) => Task.CompletedTask;

        public Task Publish(
            object message,
            Type messageType,
            CancellationToken cancellationToken = default
        ) => Task.CompletedTask;

        public Task Publish(
            object message,
            Type messageType,
            IPipe<PublishContext> publishPipe,
            CancellationToken cancellationToken = default
        ) => Task.CompletedTask;

        public Task Publish<T>(
            object values,
            CancellationToken cancellationToken = default
        )
            where T : class => Task.CompletedTask;

        public Task Publish<T>(
            object values,
            IPipe<PublishContext<T>> publishPipe,
            CancellationToken cancellationToken = default
        )
            where T : class => Task.CompletedTask;

        public Task Publish<T>(
            object values,
            IPipe<PublishContext> publishPipe,
            CancellationToken cancellationToken = default
        )
            where T : class => Task.CompletedTask;

        public ConnectHandle ConnectPublishObserver(IPublishObserver observer) =>
            throw new NotSupportedException();
    }
}
