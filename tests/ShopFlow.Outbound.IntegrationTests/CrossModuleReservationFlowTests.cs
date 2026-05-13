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
using ShopFlow.Inventory.Application.Ports;
using ShopFlow.Inventory.Infrastructure;
using ShopFlow.Inventory.Infrastructure.Consumers;
using ShopFlow.Inventory.Infrastructure.Repositories;
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
using ShopFlow.Outbound.IntegrationTests.Fixtures;
using ShopFlow.SharedKernel.Application;

namespace ShopFlow.Outbound.IntegrationTests;

/// <summary>
/// Sprint-3-redux U9 / R18 — the LOAD-BEARING cross-module reservation
/// flow test. Drives one order end-to-end through BOTH the Outbound saga
/// and the REAL Inventory consumers (Reserve / Confirm / Release) against
/// a single Postgres tenant DB hosting BOTH modules' schemas.
/// </summary>
/// <remarks>
/// <para><strong>What this proves.</strong> The Outbound saga's
/// <c>ReserveStockV1</c> publish lands in the Inventory's
/// <see cref="ReserveStockConsumer"/>, which writes real
/// <c>reservations_ledger</c> rows + decrements
/// <c>stock_items.available</c>. The consumer emits a real
/// <see cref="StockReservedV1"/> back to the saga which transitions to
/// <c>AwaitingPick</c>. The controller flow walks Order through
/// <c>Picked → Packed → AwaitingShip → Shipped</c>; on
/// <c>confirm-ship</c> the outbox carries <see cref="ConfirmStockV1"/>;
/// we re-publish it through the bus and the
/// <see cref="ConfirmStockConsumer"/> flips ledger rows
/// <c>Pending → Confirmed</c>.</para>
///
/// <para><strong>Sprint-2.5 unblocked this.</strong> Both modules'
/// migrations apply to the SAME physical Postgres database. Before the
/// per-module outbox-table prefix shipped (Sprint-2.5 U1/U2), both
/// modules carried an <c>outbox_messages</c> table that collided. With
/// <c>outbound_outbox_messages</c> / <c>inventory_outbox_messages</c> the
/// two schemas coexist cleanly.</para>
///
/// <para><strong>Bus tenant binding.</strong> The MassTransit in-memory
/// bus and the test-published envelopes are SINGLE-TENANT here (one DB,
/// one tenant). Single <see cref="RequestContext"/> is bound at host
/// construction; consumers read <see cref="IRequestContext.TenantId"/>
/// for their defense-in-depth payload-vs-envelope assertion. The K12
/// per-tenant binding filter is exercised by
/// <see cref="SagaPerTenantBindingTests"/>; here the focus is the
/// cross-module data flow on one tenant.</para>
///
/// <para>Mirrors <see cref="ShopFlow.Inbound.IntegrationTests.InboundToInventoryFlowTests"/>'s
/// shared-DB pattern; extends it to the saga side of the contract.</para>
/// </remarks>
[Collection(CrossModuleTenantCollection.Name)]
[Trait("Category", "Integration")]
public sealed class CrossModuleReservationFlowTests : IAsyncLifetime
{
    private static readonly DateTimeOffset FixedNow =
        new(2026, 5, 13, 10, 0, 0, TimeSpan.Zero);

    private readonly CrossModuleTenantFixture _fx;
    private ProvisionedCrossModuleTenant _tenant = default!;

    public CrossModuleReservationFlowTests(CrossModuleTenantFixture fx)
    {
        _fx = fx;
    }

    public async Task InitializeAsync()
    {
        _tenant = await _fx.ProvisionTenantAsync("xmod");
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task FullRoundTrip_HappyPath_ReservesShipsAndConfirmsLedger()
    {
        await CrossModuleTenantFixture.SeedStockAsync(_tenant, "SKU-A", available: 100);
        await CrossModuleTenantFixture.SeedStockAsync(_tenant, "SKU-B", available: 100);

        await using var sp = await BuildAndCaptureHostAsync();
        var harness = sp.GetRequiredService<ITestHarness>();

        // 1. POST /orders with two lines.
        var orderId = await CreateOrderViaControllerAsync(
            externalId: "ext-xmod-happy",
            lines: new[]
            {
                new CreateOrderLineRequest("SKU-A", 10, 100),
                new CreateOrderLineRequest("SKU-B", 5, 50),
            }
        );

        // 2. Publish OrderPlacedV1 → saga AwaitingReservation +
        //    publishes ReserveStockV1.
        await harness.Bus.Publish(
            new OrderPlacedV1(
                OrderId: orderId,
                TenantId: _tenant.Info.Id,
                ChannelExternalOrderId: "ext-xmod-happy",
                ShippingProfile: "standard",
                Lines: new[]
                {
                    new OrderPlacedLineV1("L1", "SKU-A", 10, 100),
                    new OrderPlacedLineV1("L2", "SKU-B", 5, 50),
                },
                OccurredAt: DateTime.UtcNow
            )
        );

        // 3. REAL ReserveStockConsumer consumes the saga's ReserveStockV1,
        //    writes 2 ledger rows + emits StockReservedV1 to
        //    inventory_outbox_messages. We substitute the Inventory's
        //    multiplexed outbox dispatcher by polling for the row +
        //    publishing it onto the bus. Then the saga's StockReserved
        //    handler runs + transitions AwaitingReservation → AwaitingPick.
        await WaitForSagaStateAsync(orderId, "AwaitingReservation");
        await WaitForLedgerCountAsync(orderId, expected: 2);
        await WaitForInventoryOutboxAsync<StockReservedV1>(orderId);
        await PublishInventoryOutboxRowAsync<StockReservedV1>(orderId);
        await WaitForSagaStateAsync(orderId, "AwaitingPick");

        // ── Mid-flow assertions: ledger + stock_items reflect the reserve.
        await using (var verify = new InventoryDbContext(_tenant.InventoryOptions))
        {
            var ledger = await verify
                .Reservations.AsNoTracking()
                .Where(r => r.OrderId == orderId.ToString())
                .ToListAsync();
            ledger.Should().HaveCount(2);
            ledger.Should().AllSatisfy(r => r.Status.ToString().Should().Be("Pending"));

            var stockRows = await verify.StockItems.AsNoTracking().ToListAsync();
            stockRows.Single(s => s.Sku.Value == "SKU-A").Available.Value.Should().Be(90);
            stockRows.Single(s => s.Sku.Value == "SKU-A").Reserved.Value.Should().Be(10);
            stockRows.Single(s => s.Sku.Value == "SKU-B").Available.Value.Should().Be(95);
            stockRows.Single(s => s.Sku.Value == "SKU-B").Reserved.Value.Should().Be(5);
        }

        // 4. Walk Order forward so the confirm-* controller pre-state
        //    guards pass (production: saga's in-process events flip the
        //    Order row; here we walk via raw SQL because the test focus
        //    is the cross-module data flow, not the Order ⇄ saga binding).
        await SetOrderStatusAsync(orderId, OrderStatus.AwaitingPick);

        // 5. POST /confirm-pick → Picked.
        await using var scope = sp.CreateAsyncScope();
        var publishEndpoint = scope.ServiceProvider.GetRequiredService<IPublishEndpoint>();
        await using (var ctrlHarness = BuildControllerHarness(publishEndpoint))
        {
            (await ctrlHarness.Controller.ConfirmPickAsync(orderId, CancellationToken.None))
                .Should()
                .BeOfType<OkObjectResult>();
        }

        // 6. POST /confirm-pack — expected 10*100 + 5*50 = 1250.
        await using (var ctrlHarness = BuildControllerHarness(publishEndpoint))
        {
            (
                await ctrlHarness.Controller.ConfirmPackAsync(
                    orderId,
                    new ConfirmPackRequest(1250),
                    CancellationToken.None
                )
            )
                .Should()
                .BeOfType<OkObjectResult>();
        }

        // 7. POST /confirm-ship — flake=0; carrier returns label; the
        //    controller enqueues ConfirmStockV1 in outbound_outbox_messages.
        await using (var ctrlHarness = BuildControllerHarness(publishEndpoint))
        {
            (await ctrlHarness.Controller.ConfirmShipAsync(orderId, CancellationToken.None))
                .Should()
                .BeOfType<OkObjectResult>();
        }

        // 8. Substitute the outbox dispatcher: read the ConfirmStockV1 row
        //    from outbound_outbox_messages, publish it on the bus. The REAL
        //    ConfirmStockConsumer consumes and transitions ledger rows
        //    Pending → Confirmed + emits StockConfirmedV1.
        await PublishOutboxRowAsync<ConfirmStockV1>(orderId);

        // Wait for the consumer to land — poll the ledger for Confirmed state.
        await WaitForLedgerStatusAsync(orderId, "Confirmed", expectedRows: 2);

        // ── Terminal assertions ─────────────────────────────────────────
        await using (var verify = new OutboundDbContext(_tenant.OutboundOptions))
        {
            var order = await verify.Orders.AsNoTracking().SingleAsync(o => o.Id == orderId);
            order.Status.Should().Be(OrderStatus.Shipped);
            order.LabelUrl.Should().NotBeNullOrEmpty();
            order.TrackingNumber.Should().NotBeNullOrEmpty();
        }

        await using (var verify = new InventoryDbContext(_tenant.InventoryOptions))
        {
            var ledger = await verify
                .Reservations.AsNoTracking()
                .Where(r => r.OrderId == orderId.ToString())
                .ToListAsync();
            ledger.Should().HaveCount(2);
            ledger.Should().AllSatisfy(r => r.Status.ToString().Should().Be("Confirmed"));

            // Stock changes hold: stock_items.reserved DECREASES on the
            // Pending → Confirmed transition (the conditional CTE inside
            // ConfirmAsync zeroes the reserved column for confirmed rows
            // because the goods physically left the warehouse). Available
            // stays at the post-reservation value (90 / 95).
            var stock = await verify.StockItems.AsNoTracking().ToListAsync();
            stock.Single(s => s.Sku.Value == "SKU-A").Reserved.Value.Should().Be(0);
            stock.Single(s => s.Sku.Value == "SKU-B").Reserved.Value.Should().Be(0);
            stock.Single(s => s.Sku.Value == "SKU-A").Available.Value.Should().Be(90);
            stock.Single(s => s.Sku.Value == "SKU-B").Available.Value.Should().Be(95);
        }
    }

    [Fact]
    public async Task FullRoundTrip_OversoldVariant_CompensatesToCancelledNoLedgerWrite()
    {
        // Plan U9 cross-module discrepancy: POST order with line qty=500
        // against stock=100. The REAL ReserveStockConsumer's atomic-CTE
        // fails → emits StockReservationFailedV1. Saga compensates via
        // Path A (release-the-empty-set) to Cancelled.
        await CrossModuleTenantFixture.SeedStockAsync(_tenant, "SKU-A", available: 100);

        await using var sp = await BuildAndCaptureHostAsync();
        var harness = sp.GetRequiredService<ITestHarness>();

        var orderId = await CreateOrderViaControllerAsync(
            externalId: "ext-xmod-oversold",
            lines: new[] { new CreateOrderLineRequest("SKU-A", 500, 100) }
        );

        await harness.Bus.Publish(
            new OrderPlacedV1(
                OrderId: orderId,
                TenantId: _tenant.Info.Id,
                ChannelExternalOrderId: "ext-xmod-oversold",
                ShippingProfile: "standard",
                Lines: new[] { new OrderPlacedLineV1("L1", "SKU-A", 500, 100) },
                OccurredAt: DateTime.UtcNow
            )
        );

        // Saga: AwaitingReservation → CompensatingReservation → Cancelled.
        // The REAL ReserveStockConsumer wrote StockReservationFailedV1 to
        // inventory_outbox_messages — we substitute the dispatcher by
        // forwarding it onto the bus.
        await WaitForInventoryOutboxAsync<StockReservationFailedV1>(orderId);
        await PublishInventoryOutboxRowAsync<StockReservationFailedV1>(orderId);

        await WaitForSagaStateAsync(orderId, "Cancelled");

        // No release publish (Path A).
        var released = harness
            .Published.Select<ReleaseStockV1>()
            .Where(p => p.Context.Message.OrderId == orderId)
            .ToList();
        released.Should().BeEmpty();

        // No ledger rows for the oversold order.
        await using var verify = new InventoryDbContext(_tenant.InventoryOptions);
        var ledger = await verify
            .Reservations.AsNoTracking()
            .CountAsync(r => r.OrderId == orderId.ToString());
        ledger.Should().Be(0);

        // Stock unchanged.
        var stock = (await verify.StockItems.AsNoTracking().ToListAsync())
            .Single(s => s.Sku.Value == "SKU-A");
        stock.Available.Value.Should().Be(100);
        stock.Reserved.Value.Should().Be(0);
    }

    // ── Harness wiring ────────────────────────────────────────────────────

    /// <summary>
    /// Build the MT host with BOTH the Outbound saga + saga repo AND the
    /// real Inventory consumers (Reserve / Confirm / Release). Both modules'
    /// DbContexts are registered as Scoped resolving against
    /// <see cref="IRequestContext.DbConnectionString"/> — single-tenant for
    /// this test, so the bound RequestContext picks the one provisioned
    /// tenant's connection string.
    /// </summary>
    private async Task<ServiceProvider> BuildHostAsync()
    {
        var services = new ServiceCollection();
        services.AddLogging(b => b.AddProvider(NullLoggerProvider.Instance));

        var rc = _tenant.BuildRequestContext();
        services.AddSingleton<RequestContext>(rc);
        services.AddSingleton<IRequestContext>(rc);
        services.AddSingleton<TimeProvider>(new FakeClock(FixedNow));

        // Outbound side — direct-bound DbContext (single tenant, no
        // per-message tenant binding needed for this test). IUnitOfWork is
        // ambiguous because BOTH modules' Application layers declare it;
        // fully-qualify the Outbound port here.
        services.AddScoped<OutboundDbContext>(_ => new OutboundDbContext(_tenant.OutboundOptions));
        services.AddScoped<IOrderRepository, OrderRepository>();
        services.AddScoped<ShopFlow.Outbound.Application.Ports.IUnitOfWork, OutboundUnitOfWork>();
        services.AddScoped<IOutboundOutbox, OutboundOutbox>();
        services.AddSingleton<IPickQueue, ShopFlow.Outbound.Infrastructure.PickQueue.PickQueue>();

        // Inventory side — DbContext + repositories the consumers need.
        services.AddScoped<InventoryDbContext>(
            _ => new InventoryDbContext(_tenant.InventoryOptions)
        );
        services.AddScoped<IReservationRepository, ReservationRepository>();
        services.AddScoped<IStockItemRepository, StockItemRepository>();
        services.AddScoped<IInboundDedupRepository, InboundDedupRepository>();

        services.AddMassTransitTestHarness(cfg =>
        {
            cfg.AddSagaStateMachine<FulfillmentSaga, FulfillmentSagaState>()
                .EntityFrameworkRepository(r =>
                {
                    r.ExistingDbContext<OutboundDbContext>();
                    r.UsePostgres();
                });

            // Real Inventory consumers — these write to reservations_ledger,
            // stock_items, and inventory_outbox_messages on the shared
            // tenant DB.
            cfg.AddConsumer<ReserveStockConsumer>();
            cfg.AddConsumer<ConfirmStockConsumer>();
            cfg.AddConsumer<ReleaseStockConsumer>();

            // Bridge consumer (Outbound side) — flips Order row to
            // Cancelled when the saga reaches its terminal Cancelled state.
            cfg.AddConsumer<OrderCancelledConsumer>();
        });

        var sp = services.BuildServiceProvider(true);
        await sp.GetRequiredService<ITestHarness>().Start();
        return sp;
    }

    private async Task<Guid> CreateOrderViaControllerAsync(
        string externalId,
        IReadOnlyList<CreateOrderLineRequest> lines
    )
    {
        await using var harness = BuildControllerHarness(new NoopPublishEndpoint());
        var result = await harness.Controller.CreateAsync(
            new CreateOrderRequest(
                ChannelExternalOrderId: externalId,
                ShippingProfile: "standard",
                Lines: lines
            ),
            CancellationToken.None
        );
        var created = result.Should().BeOfType<CreatedAtActionResult>().Subject;
        return ((OrderResponse)created.Value!).Id;
    }

    private ControllerHarness BuildControllerHarness(IPublishEndpoint publishEndpoint)
    {
        var db = new OutboundDbContext(_tenant.OutboundOptions);
        var rc = _tenant.BuildRequestContext();
        var clock = new FakeClock(FixedNow);
        var outbox = new OutboundOutbox(db, rc, clock);

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

    /// <summary>
    /// Read the outbound_outbox_messages row of type T for the given order,
    /// deserialize using <c>OutboxJsonOptions.Default</c>, and publish on
    /// the bus. Substitutes for the multiplexed outbox dispatcher's poll
    /// loop (which isn't running in this test).
    /// </summary>
    private async Task PublishOutboxRowAsync<T>(Guid orderId)
        where T : class
    {
        await using var conn = new NpgsqlConnection(_tenant.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        // payload is a jsonb column — cast to text for the LIKE match.
        cmd.CommandText = """
            SELECT event_type, payload::text
              FROM outbound_outbox_messages
             WHERE event_type LIKE @type_prefix
               AND payload::text LIKE @payload_match
            """;
        cmd.Parameters.AddWithValue("type_prefix", typeof(T).FullName + "%");
        cmd.Parameters.AddWithValue("payload_match", $"%{orderId:D}%");
        await using var reader = await cmd.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
        {
            throw new InvalidOperationException(
                $"No outbound_outbox_messages row of type {typeof(T).Name} found for order {orderId}."
            );
        }
        var eventTypeName = reader.GetString(0);
        var payload = reader.GetString(1);
        reader.Close();

        var eventType =
            Type.GetType(eventTypeName, throwOnError: false)
            ?? throw new InvalidOperationException(
                $"Outbox row references unknown type: {eventTypeName}"
            );
        var deserialized =
            System.Text.Json.JsonSerializer.Deserialize(
                payload,
                eventType,
                ShopFlow.SharedKernel.Infrastructure.OutboxJsonOptions.Default
            )
            ?? throw new InvalidOperationException(
                $"Outbox payload deserialized to null for type {eventType}"
            );

        var sp = _hostServices!;
        var harness = sp.GetRequiredService<ITestHarness>();
        await harness.Bus.Publish(deserialized, eventType);
    }

    private async Task PublishInventoryOutboxRowAsync<T>(Guid orderId)
        where T : class
    {
        await using var conn = new NpgsqlConnection(_tenant.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        // payload is jsonb — cast to text for LIKE match against the
        // serialised order id (case-insensitive via lower()).
        cmd.CommandText = """
            SELECT event_type, payload::text
              FROM inventory_outbox_messages
             WHERE event_type LIKE @type_prefix
               AND lower(payload::text) LIKE @payload_match
            """;
        cmd.Parameters.AddWithValue("type_prefix", typeof(T).FullName + "%");
        cmd.Parameters.AddWithValue("payload_match", $"%{orderId:D}%".ToLowerInvariant());
        await using var reader = await cmd.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
        {
            throw new InvalidOperationException(
                $"No inventory_outbox_messages row of type {typeof(T).Name} found for order {orderId}."
            );
        }
        var eventTypeName = reader.GetString(0);
        var payload = reader.GetString(1);
        reader.Close();

        var eventType =
            Type.GetType(eventTypeName, throwOnError: false)
            ?? throw new InvalidOperationException(
                $"Outbox row references unknown type: {eventTypeName}"
            );
        var deserialized =
            System.Text.Json.JsonSerializer.Deserialize(
                payload,
                eventType,
                ShopFlow.SharedKernel.Infrastructure.OutboxJsonOptions.Default
            )!;

        var sp = _hostServices!;
        var harness = sp.GetRequiredService<ITestHarness>();
        await harness.Bus.Publish(deserialized, eventType);
    }

    private async Task WaitForInventoryOutboxAsync<T>(Guid orderId)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (DateTime.UtcNow < deadline)
        {
            await using var conn = new NpgsqlConnection(_tenant.ConnectionString);
            await conn.OpenAsync();
            await using var cmd = conn.CreateCommand();
            // payload is jsonb — cast to text for LIKE match.
            cmd.CommandText = """
                SELECT COUNT(*)
                  FROM inventory_outbox_messages
                 WHERE event_type LIKE @type_prefix
                   AND lower(payload::text) LIKE @payload_match
                """;
            cmd.Parameters.AddWithValue("type_prefix", typeof(T).FullName + "%");
            cmd.Parameters.AddWithValue("payload_match", $"%{orderId:D}%".ToLowerInvariant());
            var count = (long)(await cmd.ExecuteScalarAsync())!;
            if (count >= 1)
            {
                return;
            }
            await Task.Delay(50);
        }
        throw new TimeoutException(
            $"inventory_outbox_messages row of type {typeof(T).Name} for order {orderId} did not appear within 10s."
        );
    }

    private async Task WaitForSagaStateAsync(Guid orderId, string expectedState)
    {
        var deadline = DateTime.UtcNow.AddSeconds(15);
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
            $"Saga {orderId} did not reach state {expectedState} within 15s."
        );
    }

    private async Task WaitForLedgerCountAsync(Guid orderId, int expected)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (DateTime.UtcNow < deadline)
        {
            await using var verify = new InventoryDbContext(_tenant.InventoryOptions);
            var count = await verify
                .Reservations.AsNoTracking()
                .CountAsync(r => r.OrderId == orderId.ToString());
            if (count >= expected)
            {
                return;
            }
            await Task.Delay(50);
        }
        throw new TimeoutException(
            $"reservations_ledger did not get {expected} rows for order {orderId} within 10s — ReserveStockConsumer likely didn't run."
        );
    }

    private async Task WaitForLedgerStatusAsync(Guid orderId, string expectedStatus, int expectedRows)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (DateTime.UtcNow < deadline)
        {
            await using var verify = new InventoryDbContext(_tenant.InventoryOptions);
            var matching = await verify
                .Reservations.AsNoTracking()
                .Where(r => r.OrderId == orderId.ToString())
                .ToListAsync();
            if (
                matching.Count == expectedRows
                && matching.All(r => r.Status.ToString() == expectedStatus)
            )
            {
                return;
            }
            await Task.Delay(50);
        }
        throw new TimeoutException(
            $"Ledger rows for order {orderId} did not reach status {expectedStatus} (x{expectedRows}) within 10s."
        );
    }

    // The harness is captured in a field so the OutboxRow publish helpers
    // can publish on the bus after disposal of the construction scope.
    private ServiceProvider? _hostServices;

    private async Task<ServiceProvider> BuildAndCaptureHostAsync()
    {
        _hostServices = await BuildHostAsync();
        return _hostServices;
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
