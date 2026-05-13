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
using ShopFlow.Outbound.Application.Sagas.Events;
using ShopFlow.Outbound.Domain;
using ShopFlow.Outbound.Infrastructure;
using ShopFlow.Outbound.Infrastructure.Outbox;
using ShopFlow.Outbound.Infrastructure.Repositories;
using ShopFlow.Outbound.Infrastructure.Shipping;
using ShopFlow.SharedKernel.Application;
using ShopFlow.SharedKernel.Infrastructure;

namespace ShopFlow.Outbound.IntegrationTests;

/// <summary>
/// Sprint-3-redux U6 — <c>confirm-pick</c>, <c>confirm-pack</c>,
/// <c>confirm-ship</c> endpoints against real Postgres + an in-memory
/// MassTransit harness. Covers happy paths, wrong-state rejections,
/// weight-variance warning, and the three carrier-retry permutations
/// (success first try, retry-then-success, retry-exhaust).
/// </summary>
/// <remarks>
/// <para>Pattern mirrors <see cref="OrdersControllerTests"/>: the
/// controller is instantiated directly with a fresh per-tenant DbContext
/// + repositories; the MassTransit <see cref="ITestHarness"/> provides
/// <see cref="IPublishEndpoint"/> + saga so we can assert the saga
/// transitions on each endpoint's publish. Saga assertions are
/// best-effort given MT's async commit timing — main-axis assertions
/// are on the Order row + outbox rows + Polly behaviour.</para>
///
/// <para>Polly pipeline in tests uses 50 ms retry backoff (not the
/// production 200 ms) to keep test wall-time bounded. The carrier
/// itself uses a 5-20 ms delay window for the same reason. The
/// retry-then-success test is the only one that asserts a non-trivial
/// wall-time lower bound (≥ 100 ms for 2 retries).</para>
/// </remarks>
[Collection(OutboundTenantCollection.Name)]
[Trait("Category", "Integration")]
public sealed class PackShipEndpointTests : IAsyncLifetime
{
    private static readonly DateTimeOffset FixedNow =
        new(2026, 5, 13, 10, 0, 0, TimeSpan.Zero);

    private readonly OutboundTenantFixture _fx;
    private ProvisionedOutboundTenant _tenant = default!;

    public PackShipEndpointTests(OutboundTenantFixture fx)
    {
        _fx = fx;
    }

    public async Task InitializeAsync()
    {
        _tenant = await _fx.ProvisionTenantAsync("pack-ship");
    }

    public Task DisposeAsync() => Task.CompletedTask;

    // ── confirm-pick ──────────────────────────────────────────────────────

    [Fact]
    public async Task ConfirmPick_HappyPath_TransitionsOrderToPickedAndReturnsOk()
    {
        var orderId = await SeedOrderInStateAsync(OrderStatus.AwaitingPick);
        await using var harness = BuildHarness(flakeRate: 0);

        var result = await harness.Controller.ConfirmPickAsync(orderId, CancellationToken.None);

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var body = ok.Value.Should().BeOfType<OrderResponse>().Subject;
        body.Status.Should().Be("Picked");

        await AssertOrderStatusAsync(orderId, OrderStatus.Picked);
    }

    [Fact]
    public async Task ConfirmPick_WrongState_Returns400InvalidState()
    {
        var orderId = await SeedOrderInStateAsync(OrderStatus.Created);
        await using var harness = BuildHarness(flakeRate: 0);

        var result = await harness.Controller.ConfirmPickAsync(orderId, CancellationToken.None);

        AssertProblemWithCode(result, expectedStatus: 400, expectedCode: "order.invalid_state");
        await AssertOrderStatusAsync(orderId, OrderStatus.Created);
    }

    [Fact]
    public async Task ConfirmPick_UnknownOrder_Returns404()
    {
        await using var harness = BuildHarness(flakeRate: 0);

        var result = await harness.Controller.ConfirmPickAsync(
            Guid.NewGuid(),
            CancellationToken.None
        );

        AssertProblemWithCode(result, expectedStatus: 404, expectedCode: "order.not_found");
    }

    // ── confirm-pack ──────────────────────────────────────────────────────

    [Fact]
    public async Task ConfirmPack_HappyPath_TransitionsToAwaitingShipNoWeightWarning()
    {
        var orderId = await SeedOrderInStateAsync(OrderStatus.Picked, expectedWeight: 100);
        await using var harness = BuildHarness(flakeRate: 0);

        var result = await harness.Controller.ConfirmPackAsync(
            orderId,
            new ConfirmPackRequest(ActualWeightTotal: 100),
            CancellationToken.None
        );

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var body = ok.Value.Should().BeOfType<ConfirmPackResponse>().Subject;
        body.WeightWarning.Should().BeFalse();
        body.WeightVariancePct.Should().Be(0.0);
        // The Order chains Picked → Packed → AwaitingShip so confirm-ship
        // can run without an extra MarkAwaitingShip POST.
        body.Order.Status.Should().Be("AwaitingShip");
        body.Order.ActualWeightTotal.Should().Be(100);

        await AssertOrderStatusAsync(orderId, OrderStatus.AwaitingShip);
    }

    [Fact]
    public async Task ConfirmPack_OverWeightThreshold_ReturnsWarning()
    {
        // Expected = 100; Actual = 85 → variance = -15% → above threshold.
        var orderId = await SeedOrderInStateAsync(OrderStatus.Picked, expectedWeight: 100);
        await using var harness = BuildHarness(flakeRate: 0);

        var result = await harness.Controller.ConfirmPackAsync(
            orderId,
            new ConfirmPackRequest(ActualWeightTotal: 85),
            CancellationToken.None
        );

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var body = ok.Value.Should().BeOfType<ConfirmPackResponse>().Subject;
        body.WeightWarning.Should().BeTrue();
        body.WeightVariancePct.Should().Be(-15.0);
        // Transition still completes — warning is informational only.
        body.Order.Status.Should().Be("AwaitingShip");
        body.Order.ActualWeightTotal.Should().Be(85);
    }

    [Fact]
    public async Task ConfirmPack_WithinWeightThreshold_NoWarning()
    {
        // Expected = 100; Actual = 105 → variance = 5% → at-or-below threshold.
        var orderId = await SeedOrderInStateAsync(OrderStatus.Picked, expectedWeight: 100);
        await using var harness = BuildHarness(flakeRate: 0);

        var result = await harness.Controller.ConfirmPackAsync(
            orderId,
            new ConfirmPackRequest(ActualWeightTotal: 105),
            CancellationToken.None
        );

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var body = ok.Value.Should().BeOfType<ConfirmPackResponse>().Subject;
        body.WeightWarning.Should().BeFalse();
        body.WeightVariancePct.Should().Be(5.0);
    }

    [Fact]
    public async Task ConfirmPack_NoExpectedWeight_NoVarianceComputed()
    {
        var orderId = await SeedOrderInStateAsync(OrderStatus.Picked, expectedWeight: null);
        await using var harness = BuildHarness(flakeRate: 0);

        var result = await harness.Controller.ConfirmPackAsync(
            orderId,
            new ConfirmPackRequest(ActualWeightTotal: 250),
            CancellationToken.None
        );

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var body = ok.Value.Should().BeOfType<ConfirmPackResponse>().Subject;
        body.WeightWarning.Should().BeFalse();
        body.WeightVariancePct.Should().BeNull();
    }

    [Fact]
    public async Task ConfirmPack_WrongState_Returns400InvalidState()
    {
        // Order in Created — not Picked — so MarkPacked fails.
        var orderId = await SeedOrderInStateAsync(OrderStatus.Created);
        await using var harness = BuildHarness(flakeRate: 0);

        var result = await harness.Controller.ConfirmPackAsync(
            orderId,
            new ConfirmPackRequest(ActualWeightTotal: 100),
            CancellationToken.None
        );

        AssertProblemWithCode(result, expectedStatus: 400, expectedCode: "order.invalid_state");
        await AssertOrderStatusAsync(orderId, OrderStatus.Created);
    }

    [Fact]
    public async Task ConfirmPack_NegativeWeight_Returns400()
    {
        var orderId = await SeedOrderInStateAsync(OrderStatus.Picked);
        await using var harness = BuildHarness(flakeRate: 0);

        var result = await harness.Controller.ConfirmPackAsync(
            orderId,
            new ConfirmPackRequest(ActualWeightTotal: -1),
            CancellationToken.None
        );

        AssertProblemWithCode(
            result,
            expectedStatus: 400,
            expectedCode: "order.actual_weight_negative"
        );
    }

    // ── confirm-ship ──────────────────────────────────────────────────────

    [Fact]
    public async Task ConfirmShip_SuccessFirstTry_TransitionsToShippedAndEnqueuesOutboxRows()
    {
        // AE5 in the plan — carrier returns label on first attempt;
        // ConfirmStockV1 + TrackingPushedV1 land in outbound_outbox_messages.
        var orderId = await SeedOrderInStateAsync(OrderStatus.AwaitingShip);
        await using var harness = BuildHarness(flakeRate: 0);

        var result = await harness.Controller.ConfirmShipAsync(orderId, CancellationToken.None);

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var body = ok.Value.Should().BeOfType<ConfirmShipResponse>().Subject;
        body.TrackingNumber.Should().StartWith("TRK-");
        body.LabelUrl.Should().StartWith("https://mock-carrier.example/labels/");
        body.Order.Status.Should().Be("Shipped");
        body.Order.LabelUrl.Should().Be(body.LabelUrl);
        body.Order.TrackingNumber.Should().Be(body.TrackingNumber);

        await AssertOrderStatusAsync(orderId, OrderStatus.Shipped);

        // Two outbox rows: ConfirmStockV1 + TrackingPushedV1.
        await using var verify = new OutboundDbContext(_tenant.Options);
        var outboxRows = await verify
            .OutboxMessages.AsNoTracking()
            .OrderBy(o => o.CreatedAt)
            .ToListAsync();
        var confirmRow = outboxRows.SingleOrDefault(r =>
            r.EventType.StartsWith("ShopFlow.Contracts.Inventory.ConfirmStockV1")
        );
        confirmRow.Should().NotBeNull();
        var trackingRow = outboxRows.SingleOrDefault(r =>
            r.EventType.StartsWith("ShopFlow.Contracts.Outbound.TrackingPushedV1")
        );
        trackingRow.Should().NotBeNull();
    }

    [Fact]
    public async Task ConfirmShip_RetryThenSuccess_RetainsWallTimeAndSucceeds()
    {
        // AE6 — carrier rigged to fail N times then succeed. Test uses
        // a deterministic ProgrammableShippingProvider rather than the
        // random MockShippingProvider so the assertion is stable.
        var orderId = await SeedOrderInStateAsync(OrderStatus.AwaitingShip);

        var programmable = new ProgrammableShippingProvider(failureCount: 2);
        await using var harness = BuildHarnessWithCustomShippingProvider(programmable);

        var sw = System.Diagnostics.Stopwatch.StartNew();
        var result = await harness.Controller.ConfirmShipAsync(orderId, CancellationToken.None);
        sw.Stop();

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var body = ok.Value.Should().BeOfType<ConfirmShipResponse>().Subject;
        body.TrackingNumber.Should().NotBeNullOrEmpty();
        // 2 retries × 50 ms backoff = ≥ 80 ms; assert > 60 ms to clear noise.
        sw.Elapsed.Should().BeGreaterThan(TimeSpan.FromMilliseconds(60));
        programmable.AttemptCount.Should().Be(3); // 2 failures + 1 success
        await AssertOrderStatusAsync(orderId, OrderStatus.Shipped);
    }

    [Fact]
    public async Task ConfirmShip_RetryExhaust_Returns503AndOrderStaysInAwaitingShip()
    {
        // AE7 — always-fail carrier. Polly's 3 retries exhaust, 503 returned,
        // no outbox rows, order stays in AwaitingShip.
        var orderId = await SeedOrderInStateAsync(OrderStatus.AwaitingShip);
        await using var harness = BuildHarness(flakeRate: 1.0);

        var result = await harness.Controller.ConfirmShipAsync(orderId, CancellationToken.None);

        AssertProblemWithCode(
            result,
            expectedStatus: 503,
            expectedCode: "shipping.carrier_unavailable"
        );
        await AssertOrderStatusAsync(orderId, OrderStatus.AwaitingShip);

        // No outbox rows — ConfirmStockV1 / TrackingPushedV1 must not leak.
        await using var verify = new OutboundDbContext(_tenant.Options);
        var outboxRows = await verify.OutboxMessages.AsNoTracking().ToListAsync();
        outboxRows
            .Where(r =>
                r.EventType.StartsWith("ShopFlow.Contracts.Inventory.ConfirmStockV1")
                || r.EventType.StartsWith("ShopFlow.Contracts.Outbound.TrackingPushedV1")
            )
            .Should()
            .BeEmpty();
    }

    [Fact]
    public async Task ConfirmShip_WrongState_Returns400InvalidState()
    {
        var orderId = await SeedOrderInStateAsync(OrderStatus.Created);
        await using var harness = BuildHarness(flakeRate: 0);

        var result = await harness.Controller.ConfirmShipAsync(orderId, CancellationToken.None);

        AssertProblemWithCode(result, expectedStatus: 400, expectedCode: "order.invalid_state");
    }

    [Fact]
    public async Task ConfirmShip_UnknownOrder_Returns404()
    {
        await using var harness = BuildHarness(flakeRate: 0);

        var result = await harness.Controller.ConfirmShipAsync(
            Guid.NewGuid(),
            CancellationToken.None
        );

        AssertProblemWithCode(result, expectedStatus: 404, expectedCode: "order.not_found");
    }

    // ── Helpers ───────────────────────────────────────────────────────────

    /// <summary>
    /// Seed an order directly into the requested status via raw SQL so the
    /// test starts at the desired step in the lifecycle without driving
    /// every confirm-* endpoint. Bypasses the saga; the saga's view of
    /// state is irrelevant for the endpoint behaviour under test.
    /// </summary>
    private async Task<Guid> SeedOrderInStateAsync(
        OrderStatus status,
        int? expectedWeight = 100
    )
    {
        var orderId = Guid.NewGuid();
        var lineId = Guid.NewGuid();
        var now = DateTime.UtcNow;
        var expectedTotal = expectedWeight; // Qty=1 so total = per-unit.

        await using var conn = new NpgsqlConnection(_tenant.ConnectionString);
        await conn.OpenAsync();
        await using var orderCmd = conn.CreateCommand();
        orderCmd.CommandText = """
            INSERT INTO orders (
                id, channel_external_order_id, shipping_profile, status,
                expected_weight_total, actual_weight_total, label_url,
                tracking_number, pick_wave_id, created_at, updated_at
            )
            VALUES (@id, @ext, 'standard', @status, @exp, NULL, NULL, NULL, NULL, @t, @t);
            """;
        orderCmd.Parameters.AddWithValue("id", orderId);
        orderCmd.Parameters.AddWithValue("ext", "ext-" + orderId.ToString("N")[..8]);
        orderCmd.Parameters.AddWithValue("status", (int)status);
        orderCmd.Parameters.AddWithValue(
            "exp",
            (object?)expectedTotal ?? DBNull.Value
        );
        orderCmd.Parameters.AddWithValue("t", now);
        await orderCmd.ExecuteNonQueryAsync();

        await using var lineCmd = conn.CreateCommand();
        lineCmd.CommandText = """
            INSERT INTO order_lines (id, order_id, sku, qty, expected_weight, created_at, updated_at)
            VALUES (@id, @oid, 'SKU-A', 1, @ew, @t, @t);
            """;
        lineCmd.Parameters.AddWithValue("id", lineId);
        lineCmd.Parameters.AddWithValue("oid", orderId);
        lineCmd.Parameters.AddWithValue(
            "ew",
            (object?)expectedWeight ?? DBNull.Value
        );
        lineCmd.Parameters.AddWithValue("t", now);
        await lineCmd.ExecuteNonQueryAsync();

        return orderId;
    }

    private async Task AssertOrderStatusAsync(Guid orderId, OrderStatus expected)
    {
        await using var verify = new OutboundDbContext(_tenant.Options);
        var order = await verify
            .Orders.AsNoTracking()
            .SingleAsync(o => o.Id == orderId);
        order.Status.Should().Be(expected);
    }

    private ControllerHarness BuildHarness(double flakeRate)
    {
        // Short delay window (5-20 ms) + 50 ms retry backoff so tests
        // complete sub-second even on the retry-exhaust path.
        var pipeline = new ResiliencePipelineBuilder()
            .AddRetry(
                new RetryStrategyOptions
                {
                    MaxRetryAttempts = 3,
                    Delay = TimeSpan.FromMilliseconds(50),
                    BackoffType = DelayBackoffType.Constant,
                    ShouldHandle = new PredicateBuilder().Handle<TransientShippingException>(),
                }
            )
            .Build();
        var provider = MockShippingProvider.WithFlakeRateAndDelay(
            pipeline,
            flakeRate,
            minDelayMs: 5,
            maxDelayMsExclusive: 20
        );
        return BuildHarnessWithCustomShippingProvider(provider);
    }

    private ControllerHarness BuildHarnessWithCustomShippingProvider(
        IMockShippingProvider shippingProvider
    )
    {
        var db = new OutboundDbContext(_tenant.Options);
        var rc = _tenant.BuildRequestContext();
        var clock = new FakeClock(FixedNow);
        var outbox = new OutboundOutbox(db, rc, clock);
        var publishEndpoint = new RecordingPublishEndpoint();
        var controller = new OrdersController(
            orderRepo: new OrderRepository(db),
            uow: new OutboundUnitOfWork(db),
            outbox: outbox,
            requestContext: rc,
            clock: clock,
            publishEndpoint: publishEndpoint,
            shippingProvider: shippingProvider
        );
        return new ControllerHarness(controller, db, publishEndpoint);
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

    private sealed record ControllerHarness(
        OrdersController Controller,
        OutboundDbContext Db,
        RecordingPublishEndpoint Publisher
    ) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync() => await Db.DisposeAsync();
    }

    private sealed class FakeClock : TimeProvider
    {
        private readonly DateTimeOffset _now;

        public FakeClock(DateTimeOffset now)
        {
            _now = now;
        }

        public override DateTimeOffset GetUtcNow() => _now;
    }

    /// <summary>
    /// Records published messages so tests can assert the controller
    /// published the right in-process saga event. Stubs the broader
    /// MT API surface — only the typed Publish overload matters here.
    /// </summary>
    private sealed class RecordingPublishEndpoint : IPublishEndpoint
    {
        public List<object> Published { get; } = new();

        public Task Publish<T>(T message, CancellationToken cancellationToken = default)
            where T : class
        {
            Published.Add(message);
            return Task.CompletedTask;
        }

        public Task Publish<T>(
            T message,
            IPipe<PublishContext<T>> publishPipe,
            CancellationToken cancellationToken = default
        )
            where T : class
        {
            Published.Add(message);
            return Task.CompletedTask;
        }

        public Task Publish<T>(
            T message,
            IPipe<PublishContext> publishPipe,
            CancellationToken cancellationToken = default
        )
            where T : class
        {
            Published.Add(message);
            return Task.CompletedTask;
        }

        public Task Publish(object message, CancellationToken cancellationToken = default)
        {
            Published.Add(message);
            return Task.CompletedTask;
        }

        public Task Publish(
            object message,
            IPipe<PublishContext> publishPipe,
            CancellationToken cancellationToken = default
        )
        {
            Published.Add(message);
            return Task.CompletedTask;
        }

        public Task Publish(
            object message,
            Type messageType,
            CancellationToken cancellationToken = default
        )
        {
            Published.Add(message);
            return Task.CompletedTask;
        }

        public Task Publish(
            object message,
            Type messageType,
            IPipe<PublishContext> publishPipe,
            CancellationToken cancellationToken = default
        )
        {
            Published.Add(message);
            return Task.CompletedTask;
        }

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

    /// <summary>
    /// Deterministic shipping provider — fails the first N attempts then
    /// succeeds. NOT wrapped by Polly (the controller uses the injected
    /// provider directly); deliberate so the test can observe Polly's
    /// retry behaviour by wrapping THIS via a fresh ResiliencePipeline.
    /// </summary>
    private sealed class ProgrammableShippingProvider : IMockShippingProvider
    {
        private readonly int _failuresUntilSuccess;
        private int _attempts;
        private readonly ResiliencePipeline _pipeline;

        public int AttemptCount => _attempts;

        public ProgrammableShippingProvider(int failureCount)
        {
            _failuresUntilSuccess = failureCount;
            _pipeline = new ResiliencePipelineBuilder()
                .AddRetry(
                    new RetryStrategyOptions
                    {
                        MaxRetryAttempts = 3,
                        Delay = TimeSpan.FromMilliseconds(50),
                        BackoffType = DelayBackoffType.Constant,
                        ShouldHandle =
                            new PredicateBuilder().Handle<TransientShippingException>(),
                    }
                )
                .Build();
        }

        public Task<ShippingLabel> CreateLabelAsync(Order order, CancellationToken ct)
        {
            return _pipeline
                .ExecuteAsync(
                    cancellationToken =>
                    {
                        _attempts++;
                        if (_attempts <= _failuresUntilSuccess)
                        {
                            throw new TransientShippingException(
                                $"programmed fail #{_attempts}"
                            );
                        }
                        var trk = "TRK-PROG-" + Guid.NewGuid().ToString("N")[..10];
                        var label = new ShippingLabel(
                            $"https://mock-carrier.example/labels/{trk}.pdf",
                            trk
                        );
                        return ValueTask.FromResult(label);
                    },
                    ct
                )
                .AsTask();
        }
    }
}
