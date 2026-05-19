using System.Text.Json;
using MassTransit;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using ShopFlow.Outbound.Api.Contracts;
using ShopFlow.Outbound.Api.Controllers;
using ShopFlow.Outbound.Application.Ports;
using ShopFlow.Outbound.Application.Queries;
using ShopFlow.Outbound.Domain;
using ShopFlow.Outbound.Infrastructure;
using ShopFlow.Outbound.Infrastructure.Outbox;
using ShopFlow.Outbound.Infrastructure.Repositories;
using ShopFlow.SharedKernel.Application;

namespace ShopFlow.Outbound.IntegrationTests;

/// <summary>
/// Sprint-7 U4 — list / KPIs / detail / transitions endpoint tests on the
/// new <c>GET</c> surfaces added to
/// <see cref="OrdersController"/>. Mirrors the existing
/// <see cref="OrdersControllerTests"/> harness pattern (direct controller
/// instantiation with a real DI-resolved <see cref="IMediator"/> bound to
/// the per-tenant <see cref="OutboundDbContext"/>) so the read paths are
/// exercised end-to-end against Testcontainers Postgres without the
/// WebApplicationFactory + ControlPlane scaffolding the full HTTP boot
/// would require.
/// </summary>
/// <remarks>
/// <para>Cross-tenant isolation is verified by provisioning two tenants
/// against the shared container, seeding orders into each, and asserting
/// that the controller (bound to tenant A's DbContext) only returns
/// tenant A's rows. This is the same DB-per-tenant boundary the
/// production routing middleware enforces; the harness short-circuits
/// the routing layer because the controller's
/// <see cref="IRequestContext"/> binds the DbContext directly.</para>
///
/// <para>Invalid ISO 8601 timestamps on <c>since</c> / <c>until</c> are
/// asserted at the controller (no DB hit) — the validation lives in
/// <see cref="OrdersController.ListAsync"/>.</para>
/// </remarks>
[Collection(OutboundTenantCollection.Name)]
[Trait("Category", "Integration")]
public sealed class OrdersListAndDetailEndpointTests : IAsyncLifetime
{
    private static readonly DateTimeOffset FixedNow =
        new(2026, 5, 19, 10, 0, 0, TimeSpan.Zero);

    private readonly OutboundTenantFixture _fx;
    private ProvisionedOutboundTenant _tenantA = default!;
    private ProvisionedOutboundTenant _tenantB = default!;

    public OrdersListAndDetailEndpointTests(OutboundTenantFixture fx)
    {
        _fx = fx;
    }

    public async Task InitializeAsync()
    {
        _tenantA = await _fx.ProvisionTenantAsync("u4-list-a");
        _tenantB = await _fx.ProvisionTenantAsync("u4-list-b");
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task ListAsync_TenantA_OnlyReturnsTenantAOrders()
    {
        // Seed two orders into tenant A and one into tenant B.
        await SeedOrderAsync(_tenantA, "SHOPEE_ORD-1", "standard");
        await SeedOrderAsync(_tenantA, "LAZADA_ORD-2", "express");
        await SeedOrderAsync(_tenantB, "TIKTOK_ORD-99", "standard");

        await using var harness = BuildHarness(_tenantA);

        var result = await harness.Controller.ListAsync(
            status: null,
            channel: null,
            search: null,
            since: null,
            until: null,
            skip: 0,
            take: 50,
            ct: CancellationToken.None);

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var body = ok.Value.Should().BeOfType<OrderListResponse>().Subject;
        body.TotalCount.Should().Be(2);
        body.Items.Should().HaveCount(2);
        body.Items.Select(i => i.ChannelExternalOrderId)
            .Should().BeEquivalentTo(new[] { "SHOPEE_ORD-1", "LAZADA_ORD-2" });
        // Channel label parsed from the prefix per Sprint-7 plan U3.
        body.Items.Single(i => i.ChannelExternalOrderId == "SHOPEE_ORD-1").Channel
            .Should().Be("Shopee");
        body.Items.Single(i => i.ChannelExternalOrderId == "LAZADA_ORD-2").Channel
            .Should().Be("Lazada");
    }

    [Fact]
    public async Task ListAsync_StatusFilter_ReturnsMatchingRowsOnly()
    {
        var aId = await SeedOrderAsync(_tenantA, "SHOPEE_FLT-A", "standard");
        var bId = await SeedOrderAsync(_tenantA, "SHOPEE_FLT-B", "standard");

        // Move order A into Reserved via the Domain state machine; the
        // legacy MarkAwaitingReservation + MarkReserved chain land the
        // row in Reserved without engaging the saga.
        await using (var db = new OutboundDbContext(_tenantA.Options))
        {
            var order = await db.Orders.SingleAsync(o => o.Id == aId);
            order.MarkAwaitingReservation();
            order.MarkReserved();
            await db.SaveChangesAsync();
        }

        await using var harness = BuildHarness(_tenantA);

        var result = await harness.Controller.ListAsync(
            status: "Reserved",
            channel: null,
            search: null,
            since: null,
            until: null,
            skip: 0,
            take: 50,
            ct: CancellationToken.None);

        var body = result
            .Should().BeOfType<OkObjectResult>()
            .Subject.Value.Should().BeOfType<OrderListResponse>().Subject;
        body.TotalCount.Should().Be(1);
        body.Items.Single().Id.Should().Be(aId);
        body.Items.Single().ChannelExternalOrderId.Should().Be("SHOPEE_FLT-A");
        // Untouched row (bId) stays in Created and is filtered out.
        body.Items.Should().NotContain(i => i.Id == bId);
    }

    [Fact]
    public async Task GetByIdAsync_KnownOrder_ReturnsFullOrderWithLines()
    {
        // The legacy GET /{id} endpoint already returns the Order shape +
        // lines; Sprint-7 U4 doesn't redefine it but the test scenario
        // gates that the legacy endpoint stays green after the controller
        // changes (ctor + MediatR / IHostEnvironment injection).
        var orderId = await SeedOrderAsync(
            _tenantA,
            channelRef: "DIRECT_DETAIL-1",
            shippingProfile: "express",
            lines: new[]
            {
                ("SKU-1", 2, (int?)100),
                ("SKU-2", 1, (int?)200),
            });

        await using var harness = BuildHarness(_tenantA);

        var result = await harness.Controller.GetByIdAsync(orderId, CancellationToken.None);

        var body = result
            .Should().BeOfType<OkObjectResult>()
            .Subject.Value.Should().BeOfType<OrderResponse>().Subject;
        body.Id.Should().Be(orderId);
        body.ChannelExternalOrderId.Should().Be("DIRECT_DETAIL-1");
        body.Lines.Should().HaveCount(2);
        body.Lines.Sum(l => l.Qty).Should().Be(3);
    }

    [Fact]
    public async Task GetTransitionsAsync_OrderWithNoAuditRows_ReturnsEmptyList()
    {
        var orderId = await SeedOrderAsync(_tenantA, "SHOPEE_NO-TR", "standard");

        await using var harness = BuildHarness(_tenantA);

        var result = await harness.Controller.GetTransitionsAsync(orderId, CancellationToken.None);

        var body = result
            .Should().BeOfType<OkObjectResult>()
            .Subject.Value.Should()
            .BeAssignableTo<IReadOnlyList<OrderTransitionDto>>().Subject;
        body.Should().BeEmpty();
    }

    [Fact]
    public async Task GetTransitionsAsync_OrderWithAuditRows_ReturnsRowsInOccurredAtOrderWithCorrelationId()
    {
        var orderId = await SeedOrderAsync(_tenantA, "SHOPEE_WITH-TR", "standard");

        // Seed two rows in outbound_saga_transitions with controlled
        // timestamps + correlation ids; the repository orders by
        // OccurredAt ASC.
        var t1 = FixedNow.UtcDateTime.AddSeconds(-30);
        var t2 = FixedNow.UtcDateTime.AddSeconds(-10);
        await using (var db = new OutboundDbContext(_tenantA.Options))
        {
            db.OrderTransitions.AddRange(
                OrderTransition.Create(
                    orderId: orderId,
                    fromState: "Initial",
                    toState: "AwaitingReservation",
                    occurredAt: t1,
                    eventType: "OrderPlacedV1",
                    correlationId: "trace-aaaa"),
                OrderTransition.Create(
                    orderId: orderId,
                    fromState: "AwaitingReservation",
                    toState: "Reserved",
                    occurredAt: t2,
                    eventType: "StockReservedV1",
                    correlationId: "trace-bbbb"));
            await db.SaveChangesAsync();
        }

        await using var harness = BuildHarness(_tenantA);

        var result = await harness.Controller.GetTransitionsAsync(orderId, CancellationToken.None);
        var body = result
            .Should().BeOfType<OkObjectResult>()
            .Subject.Value.Should()
            .BeAssignableTo<IReadOnlyList<OrderTransitionDto>>().Subject;

        body.Should().HaveCount(2);
        body[0].FromState.Should().Be("Initial");
        body[0].ToState.Should().Be("AwaitingReservation");
        body[0].CorrelationId.Should().Be("trace-aaaa");
        body[1].FromState.Should().Be("AwaitingReservation");
        body[1].ToState.Should().Be("Reserved");
        body[1].CorrelationId.Should().Be("trace-bbbb");
    }

    [Theory]
    [InlineData("not-a-date")]
    [InlineData("2026-13-99")]
    public async Task ListAsync_InvalidSince_Returns400WithCode(string badSince)
    {
        await using var harness = BuildHarness(_tenantA);

        var result = await harness.Controller.ListAsync(
            status: null,
            channel: null,
            search: null,
            since: badSince,
            until: null,
            skip: 0,
            take: 50,
            ct: CancellationToken.None);

        AssertProblemWithCode(result, expectedStatus: 400, expectedCode: "order.invalid_since");
    }

    [Theory]
    [InlineData("garbage")]
    [InlineData("2026-99-99T00:00:00Z")]
    public async Task ListAsync_InvalidUntil_Returns400WithCode(string badUntil)
    {
        await using var harness = BuildHarness(_tenantA);

        var result = await harness.Controller.ListAsync(
            status: null,
            channel: null,
            search: null,
            since: null,
            until: badUntil,
            skip: 0,
            take: 50,
            ct: CancellationToken.None);

        AssertProblemWithCode(result, expectedStatus: 400, expectedCode: "order.invalid_until");
    }

    [Fact]
    public async Task GetKpisAsync_AggregatesCountsByStatus()
    {
        // 3 orders in Created (active, not failed-today, not picking).
        await SeedOrderAsync(_tenantA, "SHOPEE_KPI-1", "standard");
        await SeedOrderAsync(_tenantA, "SHOPEE_KPI-2", "standard");
        await SeedOrderAsync(_tenantA, "SHOPEE_KPI-3", "standard");

        // 1 order pushed to AwaitingPick.
        var awaitingPickId = await SeedOrderAsync(_tenantA, "SHOPEE_KPI-AP", "standard");
        await using (var db = new OutboundDbContext(_tenantA.Options))
        {
            var order = await db.Orders.SingleAsync(o => o.Id == awaitingPickId);
            order.MarkAwaitingReservation();
            order.MarkReserved();
            order.MarkAwaitingPick();
            await db.SaveChangesAsync();
        }

        await using var harness = BuildHarness(_tenantA);

        var result = await harness.Controller.GetKpisAsync(CancellationToken.None);
        var body = result
            .Should().BeOfType<OkObjectResult>()
            .Subject.Value.Should().BeOfType<OrderKpiResponse>().Subject;

        body.AwaitingPick.Should().Be(1);
        body.AwaitingShip.Should().Be(0);
        body.FailedToday.Should().Be(0);
        // Active = total(4) - shipped(0) - cancelled(0) = 4 (includes the
        // 3 Created + 1 AwaitingPick rows).
        body.ActiveOrders.Should().Be(4);
    }

    /// <summary>
    /// Seeds an order through <see cref="Order.Create"/> + repository + UoW
    /// directly so the seed is observable to the controller via the same
    /// per-tenant DbContext binding. Returns the freshly created order id.
    /// </summary>
    private async Task<Guid> SeedOrderAsync(
        ProvisionedOutboundTenant tenant,
        string channelRef,
        string shippingProfile,
        IEnumerable<(string Sku, int Qty, int? ExpectedWeight)>? lines = null)
    {
        lines ??= new[] { ("SKU-A", 1, (int?)100) };
        await using var db = new OutboundDbContext(tenant.Options);
        var order = Order.Create(channelRef, shippingProfile, lines).Value!;
        await db.Orders.AddAsync(order);
        await db.SaveChangesAsync();
        return order.Id;
    }

    private ControllerHarness BuildHarness(ProvisionedOutboundTenant tenant)
    {
        // The harness builds a real ServiceProvider that DI-resolves the
        // MediatR pipeline against the tenant's DbContext + repositories.
        // This mirrors the per-request scope the production composition
        // creates after TenantRoutingMiddleware binds RequestContext.
        var services = new ServiceCollection();
        services.AddLogging(b => b.AddProvider(NullLoggerProvider.Instance));

        var rc = tenant.BuildRequestContext();
        services.AddSingleton<IRequestContext>(rc);
        services.AddSingleton(TimeProvider.System);

        services.AddScoped<OutboundDbContext>(_ => new OutboundDbContext(tenant.Options));
        services.AddScoped<IOrderRepository, OrderRepository>();
        services.AddScoped<IOrderTransitionRepository, OrderTransitionRepository>();
        services.AddScoped<IUnitOfWork, OutboundUnitOfWork>();
        services.AddScoped<IOutboundOutbox, OutboundOutbox>();

        // MediatR scan picks up the Outbound.Application handlers (U3).
        services.AddMediatR(cfg => cfg.RegisterServicesFromAssembly(
            typeof(ListOrdersQuery).Assembly));

        var sp = services.BuildServiceProvider();
        var scope = sp.CreateScope();

        var controller = new OrdersController(
            orderRepo: scope.ServiceProvider.GetRequiredService<IOrderRepository>(),
            uow: scope.ServiceProvider.GetRequiredService<IUnitOfWork>(),
            outbox: scope.ServiceProvider.GetRequiredService<IOutboundOutbox>(),
            requestContext: scope.ServiceProvider.GetRequiredService<IRequestContext>(),
            clock: new FakeClock(FixedNow),
            publishEndpoint: new NoopPublishEndpoint(),
            shippingProvider: new UnusedMockShippingProvider(),
            mediator: scope.ServiceProvider.GetRequiredService<IMediator>(),
            env: new TestHostEnvironment(environmentName: "Development"));
        return new ControllerHarness(controller, sp, scope);
    }

    private sealed class ControllerHarness : IAsyncDisposable
    {
        public OrdersController Controller { get; }
        private readonly ServiceProvider _sp;
        private readonly IServiceScope _scope;

        public ControllerHarness(OrdersController controller, ServiceProvider sp, IServiceScope scope)
        {
            Controller = controller;
            _sp = sp;
            _scope = scope;
        }

        public async ValueTask DisposeAsync()
        {
            _scope.Dispose();
            await _sp.DisposeAsync();
        }
    }

    private sealed class FakeClock : TimeProvider
    {
        private readonly DateTimeOffset _now;
        public FakeClock(DateTimeOffset now) { _now = now; }
        public override DateTimeOffset GetUtcNow() => _now;
    }

    private sealed class TestHostEnvironment : IHostEnvironment
    {
        public TestHostEnvironment(string environmentName)
        {
            EnvironmentName = environmentName;
        }
        public string EnvironmentName { get; set; }
        public string ApplicationName { get; set; } = "ShopFlow.Outbound.Api";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        // IsDevelopment() only reads EnvironmentName; ContentRootFileProvider
        // is never inspected by the controller path under test.
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider
        {
            get; set;
        } = new Microsoft.Extensions.FileProviders.NullFileProvider();
    }

    /// <summary>
    /// No-op publish endpoint; the read endpoints under test never publish.
    /// </summary>
    private sealed class NoopPublishEndpoint : IPublishEndpoint
    {
        public Task Publish<T>(T message, CancellationToken cancellationToken = default)
            where T : class => Task.CompletedTask;

        public Task Publish<T>(T message, IPipe<PublishContext<T>> publishPipe, CancellationToken cancellationToken = default)
            where T : class => Task.CompletedTask;

        public Task Publish<T>(T message, IPipe<PublishContext> publishPipe, CancellationToken cancellationToken = default)
            where T : class => Task.CompletedTask;

        public Task Publish(object message, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task Publish(object message, IPipe<PublishContext> publishPipe, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task Publish(object message, Type messageType, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task Publish(object message, Type messageType, IPipe<PublishContext> publishPipe, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task Publish<T>(object values, CancellationToken cancellationToken = default)
            where T : class => Task.CompletedTask;

        public Task Publish<T>(object values, IPipe<PublishContext<T>> publishPipe, CancellationToken cancellationToken = default)
            where T : class => Task.CompletedTask;

        public Task Publish<T>(object values, IPipe<PublishContext> publishPipe, CancellationToken cancellationToken = default)
            where T : class => Task.CompletedTask;

        public ConnectHandle ConnectPublishObserver(IPublishObserver observer) =>
            throw new NotSupportedException();
    }

    private sealed class UnusedMockShippingProvider : IMockShippingProvider
    {
        public Task<ShippingLabel> CreateLabelAsync(Order order, CancellationToken ct) =>
            throw new InvalidOperationException(
                "Sprint-7 U4 read endpoints should not call the shipping provider.");
    }

    private static void AssertProblemWithCode(
        IActionResult actionResult,
        int expectedStatus,
        string expectedCode)
    {
        var problem = actionResult.Should().BeOfType<ObjectResult>().Subject;
        problem.StatusCode.Should().Be(expectedStatus);
        var details = problem.Value.Should().BeAssignableTo<ProblemDetails>().Subject;
        details.Type.Should().Be($"https://shopflow.example/errors/{expectedCode}");
    }
}
