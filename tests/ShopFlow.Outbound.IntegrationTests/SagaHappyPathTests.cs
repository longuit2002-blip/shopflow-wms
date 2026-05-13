using MassTransit;
using MassTransit.Testing;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Polly;
using Polly.Retry;
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
using ShopFlow.Outbound.Infrastructure.Shipping;
using ShopFlow.SharedKernel.Application;

namespace ShopFlow.Outbound.IntegrationTests;

/// <summary>
/// Sprint-3-redux U9 — saga happy-path integration test. Drives one order
/// through the FULL Created → AwaitingReservation → Reserved → AwaitingPick
/// → Picked → Packed → AwaitingShip → Shipped chain against a real
/// Postgres tenant DB + an in-memory MassTransit harness running the
/// <see cref="FulfillmentSaga"/> with the EF saga repository.
/// </summary>
/// <remarks>
/// <para><strong>Saga-throughput correctness gate (closes U8 saga-bypass).</strong>
/// U8's W5 scale gate auto-driver short-circuits the saga's reservation /
/// compensation hops via raw-SQL Order.status updates so the harness can
/// measure pipeline throughput without running 3 concurrent saga instances.
/// This test fills the resulting gap: ONE order, full saga end-to-end,
/// asserting the saga reaches Shipped + outbox carries the expected
/// cross-module events.</para>
///
/// <para><strong>Stub Inventory side.</strong> The Inventory consumers
/// (Reserve / Confirm / Release) live in <c>ShopFlow.Inventory.Infrastructure</c>
/// and would require the Inventory migrations + a populated
/// <c>stock_items</c> ledger to round-trip. For this happy-path test the
/// Inventory side is simulated by re-publishing the side-effect events
/// (<see cref="StockReservedV1"/>) directly on the bus. The cross-module
/// REAL-consumer integration is covered by <see cref="CrossModuleReservationFlowTests"/>;
/// here we exercise the saga state machine + the Order aggregate's
/// state-machine + the controller endpoints + the outbox writes.</para>
///
/// <para>Mirrors <see cref="PickFailureCompensationTests"/>'s harness shape
/// (saga repo against real Postgres, in-memory bus, controller invoked
/// directly with per-request DbContext).</para>
/// </remarks>
[Collection(OutboundTenantCollection.Name)]
[Trait("Category", "Integration")]
public sealed class SagaHappyPathTests : IAsyncLifetime
{
    private static readonly DateTimeOffset FixedNow =
        new(2026, 5, 13, 10, 0, 0, TimeSpan.Zero);

    private readonly OutboundTenantFixture _fx;
    private ProvisionedOutboundTenant _tenant = default!;

    public SagaHappyPathTests(OutboundTenantFixture fx)
    {
        _fx = fx;
    }

    public async Task InitializeAsync()
    {
        _tenant = await _fx.ProvisionTenantAsync("saga-happy");
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task FullSaga_OrderPlacedToShipped_EndsAtShippedWithLabelAndOutboxEvents()
    {
        await using var sp = await BuildHostAsync();
        var harness = sp.GetRequiredService<ITestHarness>();

        // 1. POST /orders — creates Order in Created state + enqueues
        //    OrderPlacedV1 in the outbox row (atomic with the insert).
        var orderId = await CreateOrderViaControllerAsync("ext-happy-1");

        // 2. Publish OrderPlacedV1 directly (substitute for the outbox
        //    dispatcher's poll loop) — the saga's initial event handler
        //    transitions Created → AwaitingReservation and publishes
        //    ReserveStockV1.
        await harness.Bus.Publish(
            new OrderPlacedV1(
                OrderId: orderId,
                TenantId: _tenant.Info.Id,
                ChannelExternalOrderId: "ext-happy-1",
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

        // 3. Saga published ReserveStockV1 — verify it appears on the bus.
        var reservePublished = await harness.Published.Any<ReserveStockV1>(
            p => p.Context.Message.OrderId == orderId
        );
        reservePublished.Should().BeTrue("saga must publish ReserveStockV1 from AwaitingReservation");

        // 4. Stub Inventory side: emit StockReservedV1 immediately. The
        //    saga's StockReserved handler transitions Reserved → AwaitingPick
        //    after enqueuing a PickRequest on the in-process IPickQueue.
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

        // 5. Drive the Order aggregate forward in lockstep with the saga so
        //    the controller endpoint's pre-state guards pass. In production
        //    the saga's in-process events flip the Order row; here the test
        //    walks the aggregate via raw SQL so the U9 happy-path exercises
        //    the controller body without re-implementing the entire
        //    saga→controller binding wire (U6's PackShipEndpointTests covers
        //    each endpoint's body individually).
        await SetOrderStatusAsync(orderId, OrderStatus.AwaitingPick);

        // 6. POST /confirm-pick → Order to Picked; saga to Picked via the
        //    in-process PickConfirmed publish.
        await using var scope = sp.CreateAsyncScope();
        var publishEndpoint = scope.ServiceProvider.GetRequiredService<IPublishEndpoint>();
        await using (var ctrlHarness = BuildControllerHarness(publishEndpoint))
        {
            var pickResult = await ctrlHarness.Controller.ConfirmPickAsync(
                orderId,
                CancellationToken.None
            );
            pickResult.Should().BeOfType<OkObjectResult>();
        }
        await WaitForSagaStateAsync(orderId, "Picked");

        // 7. POST /confirm-pack with actual_weight == expected_weight
        //    (qty=1 + expected_weight=100 per line × 2 lines = 200).
        //    Order: Picked → Packed → AwaitingShip (controller chains
        //    both transitions in one SaveChanges). Saga: Picked → Packed.
        await using (var ctrlHarness = BuildControllerHarness(publishEndpoint))
        {
            var packResult = await ctrlHarness.Controller.ConfirmPackAsync(
                orderId,
                new ConfirmPackRequest(150), // 100 + 50 = 150
                CancellationToken.None
            );
            packResult.Should().BeOfType<OkObjectResult>();
        }
        await WaitForSagaStateAsync(orderId, "Packed");

        // 8. POST /confirm-ship — carrier mock always-succeeds (flake=0).
        //    Order: AwaitingShip → Shipped + label/tracking persisted +
        //    ConfirmStockV1 + TrackingPushedV1 enqueued in outbound_outbox_messages.
        //    Saga: Packed → Shipped (terminal).
        await using (var ctrlHarness = BuildControllerHarness(publishEndpoint))
        {
            var shipResult = await ctrlHarness.Controller.ConfirmShipAsync(
                orderId,
                CancellationToken.None
            );
            shipResult.Should().BeOfType<OkObjectResult>();
        }
        await WaitForSagaStateAsync(orderId, "Shipped");

        // ── Assertions ──────────────────────────────────────────────────
        // Order row reached Shipped + carries label_url + tracking_number.
        await using (var verify = new OutboundDbContext(_tenant.Options))
        {
            var order = await verify.Orders.AsNoTracking().SingleAsync(o => o.Id == orderId);
            order.Status.Should().Be(OrderStatus.Shipped);
            order.LabelUrl.Should().NotBeNullOrEmpty();
            order.TrackingNumber.Should().NotBeNullOrEmpty();
        }

        // outbound_outbox_messages carries ConfirmStockV1 + TrackingPushedV1.
        await using (var verify = new OutboundDbContext(_tenant.Options))
        {
            var outbox = await verify.OutboxMessages.AsNoTracking().ToListAsync();
            outbox.Should().Contain(o =>
                o.EventType.StartsWith("ShopFlow.Contracts.Inventory.ConfirmStockV1")
            );
            outbox.Should().Contain(o =>
                o.EventType.StartsWith("ShopFlow.Contracts.Outbound.TrackingPushedV1")
            );
        }
    }

    [Fact]
    public async Task IdempotentOrderPost_DuplicateChannelExternalOrderId_ReturnsSameOrderIdSagaStartsOnce()
    {
        // Plan U9 idempotency scenario: POST /orders with the same
        // channel_external_order_id twice. First POST returns 201 with a
        // new order id; second POST returns 200 with the SAME order id;
        // saga starts only once (one saga_state row).
        await using var sp = await BuildHostAsync();
        var harness = sp.GetRequiredService<ITestHarness>();

        const string externalId = "ext-idempotent";

        var firstOrderId = await CreateOrderViaControllerAsync(externalId);
        var secondOrderId = await CreateOrderViaControllerAsync(externalId, expectIdempotent: true);
        secondOrderId.Should().Be(
            firstOrderId,
            "duplicate POST should return the same order id (controller idempotency short-circuit)"
        );

        // Publish OrderPlacedV1 ONCE — that's what the outbox dispatcher
        // would do (one row → one publish). The saga must end up with
        // exactly one saga_state row.
        await harness.Bus.Publish(
            new OrderPlacedV1(
                OrderId: firstOrderId,
                TenantId: _tenant.Info.Id,
                ChannelExternalOrderId: externalId,
                ShippingProfile: "standard",
                Lines: new[] { new OrderPlacedLineV1("L1", "SKU-A", 1, 100) },
                OccurredAt: DateTime.UtcNow
            )
        );
        await WaitForSagaStateAsync(firstOrderId, "AwaitingReservation");

        // Database has exactly one Order row + one saga_state row.
        await using var verify = new OutboundDbContext(_tenant.Options);
        var orderCount = await verify.Orders.CountAsync(o => o.ChannelExternalOrderId == externalId);
        orderCount.Should().Be(1);

        await using var conn = new NpgsqlConnection(_tenant.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """SELECT COUNT(*) FROM saga_state WHERE "CorrelationId" = @oid""";
        cmd.Parameters.AddWithValue("oid", firstOrderId);
        var sagaCount = (long)(await cmd.ExecuteScalarAsync())!;
        sagaCount.Should().Be(1);
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

        // U5 — IPickQueue is required by the saga's StockReserved Then
        // handler. The PickWaveGeneratorService is NOT started in this test
        // (PickWaveBatchingFlowTests covers the wave generation path).
        services.AddSingleton<IPickQueue, ShopFlow.Outbound.Infrastructure.PickQueue.PickQueue>();

        services.AddMassTransitTestHarness(cfg =>
        {
            cfg.AddSagaStateMachine<FulfillmentSaga, FulfillmentSagaState>()
                .EntityFrameworkRepository(r =>
                {
                    r.ExistingDbContext<OutboundDbContext>();
                    r.UsePostgres();
                });

            // Bridge consumer that flips the Order row to Cancelled on the
            // saga's terminal Cancelled state — registered for completeness
            // even though this happy-path test never enters the Cancelled
            // state. (Mirrors production's AddConsumers(asm) discovery.)
            cfg.AddConsumer<OrderCancelledConsumer>();
        });

        var sp = services.BuildServiceProvider(true);
        await sp.GetRequiredService<ITestHarness>().Start();
        return sp;
    }

    private async Task<Guid> CreateOrderViaControllerAsync(
        string externalId,
        bool expectIdempotent = false
    )
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
        if (expectIdempotent)
        {
            var ok = result.Should().BeOfType<OkObjectResult>().Subject;
            return ((OrderResponse)ok.Value!).Id;
        }
        var created = result.Should().BeOfType<CreatedAtActionResult>().Subject;
        return ((OrderResponse)created.Value!).Id;
    }

    private ControllerHarness BuildControllerHarness(IPublishEndpoint publishEndpoint)
    {
        var db = new OutboundDbContext(_tenant.Options);
        var rc = _tenant.BuildRequestContext();
        var clock = new FakeClock(FixedNow);
        var outbox = new OutboundOutbox(db, rc, clock);

        // Always-succeed carrier with 5-20 ms delay — well under the saga's
        // default 30s message timeout; keeps PR-time wall-time bounded.
        var pipeline = new ResiliencePipelineBuilder()
            .AddRetry(
                new RetryStrategyOptions
                {
                    MaxRetryAttempts = 1,
                    Delay = TimeSpan.FromMilliseconds(50),
                    BackoffType = DelayBackoffType.Constant,
                    ShouldHandle = new PredicateBuilder().Handle<TransientShippingException>(),
                }
            )
            .Build();
        var shippingProvider = MockShippingProvider.WithFlakeRateAndDelay(
            pipeline,
            flakeRate: 0,
            minDelayMs: 5,
            maxDelayMsExclusive: 20
        );

        var controller = new OrdersController(
            orderRepo: new OrderRepository(db),
            uow: new OutboundUnitOfWork(db),
            outbox: outbox,
            requestContext: rc,
            clock: clock,
            publishEndpoint: publishEndpoint,
            shippingProvider: shippingProvider
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
    /// No-op publish endpoint used by the controller during the initial
    /// POST so the OrderPlacedV1 doesn't get double-published (we then
    /// publish OrderPlacedV1 manually onto the bus to feed the saga).
    /// </summary>
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
