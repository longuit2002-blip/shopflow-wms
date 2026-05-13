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
using ShopFlow.Outbound.Application.Sagas.Events;
using ShopFlow.Outbound.Domain;
using ShopFlow.Outbound.Infrastructure;
using ShopFlow.Outbound.Infrastructure.Consumers;
using ShopFlow.Outbound.Infrastructure.Outbox;
using ShopFlow.Outbound.Infrastructure.Repositories;
using ShopFlow.SharedKernel.Application;

namespace ShopFlow.Outbound.IntegrationTests;

/// <summary>
/// Sprint-3-redux U7 — end-to-end pick-failure compensation flow against
/// a real Postgres tenant DB + an in-memory MassTransit harness running
/// both the <see cref="FulfillmentSaga"/> (with the EF saga repository
/// against the tenant DB) and the <see cref="OrderCancelledConsumer"/>
/// (the bridge that flips the Order row from
/// <c>CompensatingReservation</c> to <c>Cancelled</c> after the saga's
/// terminal-state on-enter activity publishes
/// <see cref="OrderCancelled"/>).
/// </summary>
/// <remarks>
/// <para>What this test does NOT cover: the Inventory side's
/// <c>ReleaseStockConsumer</c>. That consumer lives in
/// <c>ShopFlow.Inventory.Infrastructure</c> + needs the Inventory
/// migrations + a populated <c>stock_items</c>/<c>reservations</c>
/// ledger to round-trip <c>ReleaseStockV1 → StockReleasedV1</c>. The
/// full two-module integration is covered by U9's
/// <c>CrossModuleReservationFlowTests</c>; this test simulates the
/// Inventory side by re-publishing <see cref="StockReleasedV1"/>
/// directly on the bus once the saga's <see cref="ReleaseStockV1"/>
/// arrives. The boundary under test here is the **saga ⇄ Order
/// aggregate** R3 eventual-consistency loop, not the cross-module
/// reservation ledger.</para>
///
/// <para>Plan U7 scenario covered: "End-to-end against Testcontainers
/// Postgres + InMemory MT: POST /orders → drive to AwaitingPick → POST
/// mark-pick-failed → wait for saga to publish ReleaseStockV1 → consumer
/// emits StockReleasedV1 → saga transitions to Cancelled → assert Order
/// row status='Cancelled'." Stock-items / reservation assertions are
/// the U9 + U8 cross-module gate's job.</para>
/// </remarks>
[Collection(OutboundTenantCollection.Name)]
[Trait("Category", "Integration")]
public sealed class PickFailureCompensationTests : IAsyncLifetime
{
    private static readonly DateTimeOffset FixedNow =
        new(2026, 5, 13, 10, 0, 0, TimeSpan.Zero);

    private readonly OutboundTenantFixture _fx;
    private ProvisionedOutboundTenant _tenant = default!;

    public PickFailureCompensationTests(OutboundTenantFixture fx)
    {
        _fx = fx;
    }

    public async Task InitializeAsync()
    {
        _tenant = await _fx.ProvisionTenantAsync("pick-fail");
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task PickFailed_FullFlow_PublishesReleaseStockAndTransitionsSagaToCancelled()
    {
        // End-to-end: real EF saga repo against tenant Postgres + in-memory
        // bus. The Inventory side is stubbed by re-publishing the
        // StockReleasedV1 that ReleaseStockConsumer would emit (U9 covers
        // the real consumer path).
        await using var sp = await BuildHostAsync();
        var harness = sp.GetRequiredService<ITestHarness>();

        // 1. POST /orders — creates Order in Created state + enqueues
        //    OrderPlacedV1 in the outbox.
        var orderId = await CreateOrderViaControllerAsync();

        // 2. The saga listens for OrderPlacedV1 on the bus. The controller
        //    enqueued it in the outbox; for the test we publish it directly
        //    (the U1 outbox dispatcher's poll loop isn't running here).
        await harness.Bus.Publish(
            new OrderPlacedV1(
                OrderId: orderId,
                TenantId: _tenant.Info.Id,
                ChannelExternalOrderId: "ext-comp-flow",
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

        // 3. StockReservedV1 lands; saga progresses through
        //    Reserved → AwaitingPick (U5 chain).
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

        // 4. Drive the Order aggregate forward so its Status matches the
        //    saga (Created → AwaitingReservation → Reserved → AwaitingPick).
        //    In production the saga's on-enter activities drive these via
        //    in-process events; here the test just walks the aggregate
        //    through to set up MarkCompensatingReservation's pre-state.
        await DriveOrderToAwaitingPickAsync(orderId);

        // 5. POST /mark-pick-failed — Order goes to CompensatingReservation
        //    and PickFailed is published; the saga transitions to
        //    CompensatingReservation and publishes ReleaseStockV1.
        await using var scope = sp.CreateAsyncScope();
        var publishEndpoint = scope.ServiceProvider.GetRequiredService<IPublishEndpoint>();
        var pickFailResult = await CallMarkPickFailedAsync(
            orderId,
            new MarkPickFailedRequest("physical discrepancy"),
            publishEndpoint
        );
        pickFailResult.Should().BeOfType<OkObjectResult>();
        await AssertOrderStatusAsync(orderId, OrderStatus.CompensatingReservation);

        await WaitForSagaStateAsync(orderId, "CompensatingReservation");

        // 6. Assert the saga published ReleaseStockV1 with both line ids.
        var released = harness
            .Published.Select<ReleaseStockV1>()
            .Where(p => p.Context.Message.OrderId == orderId)
            .ToList();
        released.Should().HaveCount(1, "Path B publishes ONE multi-line ReleaseStockV1");
        released
            .Single()
            .Context.Message.OrderLineIds.Should()
            .BeEquivalentTo(new[] { "L1", "L2" });

        // 7. Stub the Inventory side: emit the StockReleasedV1 that
        //    ReleaseStockConsumer would have emitted.
        await harness.Bus.Publish(
            new StockReleasedV1(
                OrderId: orderId,
                TenantId: _tenant.Info.Id,
                OrderLineIds: new[] { "L1", "L2" },
                OccurredAt: DateTime.UtcNow
            )
        );

        // 8. Saga should reach Cancelled; the on-enter activity publishes
        //    OrderCancelled; the registered OrderCancelledConsumer (a real
        //    consumer wired in BuildHostAsync) marks the Order row as
        //    Cancelled in the tenant DB (R3 eventual-consistency boundary).
        await WaitForSagaStateAsync(orderId, "Cancelled");
        await WaitForOrderStatusAsync(orderId, OrderStatus.Cancelled);
    }

    [Fact]
    public async Task PickFailed_OnWrongState_Returns400AndDoesNotPublishPickFailedEvent()
    {
        // Mark-pick-failed on an Order in Created state returns 400 from
        // Order.MarkCompensatingReservation's invariant check. No PickFailed
        // event reaches the saga; no ReleaseStockV1 is published.
        await using var sp = await BuildHostAsync();
        var harness = sp.GetRequiredService<ITestHarness>();

        var orderId = await CreateOrderViaControllerAsync();
        // Don't drive the order forward; it stays in Created.

        await using var scope = sp.CreateAsyncScope();
        var publishEndpoint = scope.ServiceProvider.GetRequiredService<IPublishEndpoint>();
        var result = await CallMarkPickFailedAsync(
            orderId,
            new MarkPickFailedRequest("operator typo"),
            publishEndpoint
        );

        AssertProblemWithCode(result, expectedStatus: 400, expectedCode: "order.invalid_state");
        await AssertOrderStatusAsync(orderId, OrderStatus.Created);

        // No ReleaseStockV1 published — the saga never received PickFailed.
        await Task.Delay(200);
        var released = harness
            .Published.Select<ReleaseStockV1>()
            .Where(p => p.Context.Message.OrderId == orderId)
            .ToList();
        released.Should().BeEmpty();
    }

    [Fact]
    public async Task PickFailed_DuplicatePostInCompensatingState_Returns409()
    {
        // Race: operator hits mark-pick-failed twice. First POST flips Order
        // to CompensatingReservation; second POST should return 409 conflict
        // because the controller's pre-state guard catches the
        // CompensatingReservation / Cancelled states explicitly.
        await using var sp = await BuildHostAsync();
        var harness = sp.GetRequiredService<ITestHarness>();
        await using var scope = sp.CreateAsyncScope();
        var publishEndpoint = scope.ServiceProvider.GetRequiredService<IPublishEndpoint>();

        var orderId = await CreateOrderViaControllerAsync();
        await DriveOrderToAwaitingPickAsync(orderId);

        var first = await CallMarkPickFailedAsync(
            orderId,
            new MarkPickFailedRequest("first attempt"),
            publishEndpoint
        );
        first.Should().BeOfType<OkObjectResult>();
        await AssertOrderStatusAsync(orderId, OrderStatus.CompensatingReservation);

        var second = await CallMarkPickFailedAsync(
            orderId,
            new MarkPickFailedRequest("second attempt"),
            publishEndpoint
        );
        AssertProblemWithCode(
            second,
            expectedStatus: 409,
            expectedCode: "order.pick_failure_already_recorded"
        );
    }

    // ── Harness wiring ────────────────────────────────────────────────────

    /// <summary>
    /// Build the test host: scoped <see cref="RequestContext"/> bound to
    /// the provisioned tenant, scoped <see cref="OutboundDbContext"/>
    /// against the tenant's connection string, MassTransit test harness
    /// with the EF saga repo + auto-registered <see cref="OrderCancelledConsumer"/>.
    /// </summary>
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

        // U5 — IPickQueue Singleton, required by the saga's StockReserved
        // Then handler. Background generator is NOT started in this test
        // (we only need the queue resolution to succeed).
        services.AddSingleton<IPickQueue, ShopFlow.Outbound.Infrastructure.PickQueue.PickQueue>();

        services.AddMassTransitTestHarness(cfg =>
        {
            cfg.AddSagaStateMachine<FulfillmentSaga, FulfillmentSagaState>()
                .EntityFrameworkRepository(r =>
                {
                    r.ExistingDbContext<OutboundDbContext>();
                    r.UsePostgres();
                });

            // U7 — OrderCancelledConsumer is the bridge that flips the
            // Order row to Cancelled after the saga's terminal-state
            // OrderCancelled publish.
            cfg.AddConsumer<OrderCancelledConsumer>();
        });

        var sp = services.BuildServiceProvider(true);
        await sp.GetRequiredService<ITestHarness>().Start();
        return sp;
    }

    /// <summary>
    /// Create an order via the controller, returning the new id.
    /// </summary>
    private async Task<Guid> CreateOrderViaControllerAsync()
    {
        var harness = BuildControllerHarness(publishEndpoint: new NoopPublishEndpoint());
        var result = await harness.Controller.CreateAsync(
            new CreateOrderRequest(
                ChannelExternalOrderId: "ext-comp-flow",
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
        var body = created.Value.Should().BeOfType<OrderResponse>().Subject;
        await harness.Db.DisposeAsync();
        return body.Id;
    }

    /// <summary>
    /// Drive the Order aggregate through Created → AwaitingReservation →
    /// Reserved → AwaitingPick directly via the repository (bypasses the
    /// saga for setup). Mirrors the state the controller would observe
    /// after the saga walks through those transitions and the in-process
    /// events flow back to flip the Order row.
    /// </summary>
    private async Task DriveOrderToAwaitingPickAsync(Guid orderId)
    {
        // Use raw SQL — Order doesn't expose unconditional setters and
        // walking the full Domain state machine here is incidental to U7's
        // assertion. The U7 test exercises the saga's compensation logic,
        // not the Created → AwaitingPick happy path (that's U2/U6).
        await using var conn = new NpgsqlConnection(_tenant.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE orders SET status = @status WHERE id = @id";
        cmd.Parameters.AddWithValue("status", (int)OrderStatus.AwaitingPick);
        cmd.Parameters.AddWithValue("id", orderId);
        var rows = await cmd.ExecuteNonQueryAsync();
        rows.Should().Be(1);
    }

    private async Task<IActionResult> CallMarkPickFailedAsync(
        Guid orderId,
        MarkPickFailedRequest request,
        IPublishEndpoint publishEndpoint
    )
    {
        var harness = BuildControllerHarness(publishEndpoint);
        var result = await harness.Controller.MarkPickFailedAsync(
            orderId,
            request,
            CancellationToken.None
        );
        await harness.Db.DisposeAsync();
        return result;
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

    // ── Assertion helpers ─────────────────────────────────────────────────

    private async Task AssertOrderStatusAsync(Guid orderId, OrderStatus expected)
    {
        await using var verify = new OutboundDbContext(_tenant.Options);
        var order = await verify
            .Orders.AsNoTracking()
            .SingleAsync(o => o.Id == orderId);
        order.Status.Should().Be(expected);
    }

    private async Task WaitForOrderStatusAsync(Guid orderId, OrderStatus expected)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (DateTime.UtcNow < deadline)
        {
            await using var verify = new OutboundDbContext(_tenant.Options);
            var order = await verify
                .Orders.AsNoTracking()
                .SingleAsync(o => o.Id == orderId);
            if (order.Status == expected)
            {
                return;
            }
            await Task.Delay(100);
        }
        throw new TimeoutException(
            $"Order {orderId} did not reach status {expected} within 10s."
        );
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
            await Task.Delay(100);
        }
        throw new TimeoutException(
            $"Saga {orderId} did not reach state {expectedState} within 10s."
        );
    }

    private static void AssertProblemWithCode(
        IActionResult actionResult,
        int expectedStatus,
        string expectedCode
    )
    {
        var problem = actionResult.Should().BeOfType<ObjectResult>().Subject;
        problem.StatusCode.Should().Be(expectedStatus);
        var details = problem.Value.Should().BeAssignableTo<ProblemDetails>().Subject;
        details.Type.Should().Be($"https://shopflow.example/errors/{expectedCode}");
    }

    // ── Test types ────────────────────────────────────────────────────────

    private sealed record ControllerHarness(OrdersController Controller, OutboundDbContext Db);

    private sealed class FakeClock : TimeProvider
    {
        private readonly DateTimeOffset _now;

        public FakeClock(DateTimeOffset now) => _now = now;

        public override DateTimeOffset GetUtcNow() => _now;
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

    private sealed class UnusedMockShippingProvider : IMockShippingProvider
    {
        public Task<ShippingLabel> CreateLabelAsync(Order order, CancellationToken ct) =>
            throw new InvalidOperationException(
                "U7 PickFailureCompensationTests should not reach the shipping path."
            );
    }
}
