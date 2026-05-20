using System.Text.Json;
using MassTransit;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
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
/// Sprint-7 U4 — dev-mode <c>POST /seed</c> endpoint tests. Two scenarios:
/// in Development the endpoint creates an order, appends a synthesized
/// <c>OrderPlacedV1</c> to the outbox, and returns 201; outside
/// Development it returns 404 with <c>environment_not_dev</c>.
/// </summary>
/// <remarks>
/// <para>Pattern mirrors <see cref="OrdersListAndDetailEndpointTests"/> —
/// direct controller instantiation with a tenant-bound DbContext +
/// scope-resolved MediatR + a controllable <see cref="IHostEnvironment"/>
/// stub. The outbox-row assertion reads <c>outbound_outbox_messages</c>
/// directly through a fresh <see cref="OutboundDbContext"/> so the
/// dispatcher's polling path is not exercised; verifying the row landed
/// is sufficient to assert the controller wrote it (the dispatcher is
/// covered by Sprint-3-redux U1's hosted-service tests).</para>
/// </remarks>
[Collection(OutboundTenantCollection.Name)]
[Trait("Category", "Integration")]
public sealed class OrdersSeedEndpointTests : IAsyncLifetime
{
    private static readonly DateTimeOffset FixedNow =
        new(2026, 5, 19, 10, 0, 0, TimeSpan.Zero);

    private readonly OutboundTenantFixture _fx;
    private ProvisionedOutboundTenant _tenant = default!;

    public OrdersSeedEndpointTests(OutboundTenantFixture fx)
    {
        _fx = fx;
    }

    public async Task InitializeAsync()
    {
        _tenant = await _fx.ProvisionTenantAsync("u4-seed");
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task SeedAsync_InDevelopment_Creates201PlusOrderPlacedV1OutboxRow()
    {
        await using var harness = BuildHarness(environmentName: "Development");

        var result = await harness.Controller.SeedAsync(
            request: new SeedOrderRequest(LineCount: 3, ChannelPrefix: "SEED_"),
            idempotencyKey: "ULID-IDEM-1",
            ct: CancellationToken.None);

        var created = result.Should().BeOfType<CreatedAtActionResult>().Subject;
        created.ActionName.Should().Be(nameof(OrdersController.GetByIdAsync));
        var body = created.Value.Should().BeOfType<OrderResponse>().Subject;
        body.ShippingProfile.Should().Be("standard");
        body.ChannelExternalOrderId.Should().StartWith("SEED_");
        body.Status.Should().Be("Created");
        body.Lines.Should().HaveCount(3);
        body.Lines.Select(l => l.Sku).Should().BeEquivalentTo(new[]
        {
            "SEED-SKU-1", "SEED-SKU-2", "SEED-SKU-3",
        });
        body.Lines.Should().AllSatisfy(l =>
        {
            l.Qty.Should().Be(1);
            l.ExpectedWeight.Should().Be(100);
        });
        body.ExpectedWeightTotal.Should().Be(300);

        // Verify the OrderPlacedV1 outbox row landed alongside the order
        // in the same physical DB transaction.
        await using var verify = new OutboundDbContext(_tenant.Options);
        var orderRow = await verify.Orders
            .Include(o => o.Lines)
            .SingleAsync(o => o.Id == body.Id);
        orderRow.Lines.Should().HaveCount(3);

        var outboxRows = await verify.OutboxMessages
            .AsNoTracking()
            .Where(o => o.EventType.StartsWith("ShopFlow.Contracts.Outbound.OrderPlacedV1"))
            .ToListAsync();
        outboxRows.Should().HaveCount(1);
        var row = outboxRows.Single();
        row.TenantId.Should().Be(_tenant.Info.Id);

        using var doc = JsonDocument.Parse(row.Payload);
        doc.RootElement.GetProperty("orderId").GetGuid().Should().Be(body.Id);
        doc.RootElement.GetProperty("shippingProfile").GetString().Should().Be("standard");
        doc.RootElement.GetProperty("lines").GetArrayLength().Should().Be(3);
    }

    [Fact]
    public async Task SeedAsync_NullBody_UsesDefaultLineCount3()
    {
        await using var harness = BuildHarness(environmentName: "Development");

        var result = await harness.Controller.SeedAsync(
            request: null,
            idempotencyKey: null,
            ct: CancellationToken.None);

        var body = result
            .Should().BeOfType<CreatedAtActionResult>()
            .Subject.Value.Should().BeOfType<OrderResponse>().Subject;
        body.Lines.Should().HaveCount(3);
        body.ChannelExternalOrderId.Should().StartWith("SEED_");
    }

    [Fact]
    public async Task SeedAsync_LineCountOver50_ClampedTo50()
    {
        await using var harness = BuildHarness(environmentName: "Development");

        var result = await harness.Controller.SeedAsync(
            request: new SeedOrderRequest(LineCount: 999),
            idempotencyKey: null,
            ct: CancellationToken.None);

        var body = result
            .Should().BeOfType<CreatedAtActionResult>()
            .Subject.Value.Should().BeOfType<OrderResponse>().Subject;
        body.Lines.Should().HaveCount(50);
    }

    [Theory]
    [InlineData("Production")]
    [InlineData("Staging")]
    [InlineData("Test")]
    public async Task SeedAsync_OutsideDevelopment_Returns404WithEnvironmentNotDev(string envName)
    {
        await using var harness = BuildHarness(environmentName: envName);

        var result = await harness.Controller.SeedAsync(
            request: new SeedOrderRequest(),
            idempotencyKey: null,
            ct: CancellationToken.None);

        var problem = result.Should().BeOfType<ObjectResult>().Subject;
        problem.StatusCode.Should().Be(404);
        var details = problem.Value.Should().BeAssignableTo<ProblemDetails>().Subject;
        details.Type.Should().Be("https://shopflow.example/errors/environment_not_dev");

        // Defensive: no order row landed.
        await using var verify = new OutboundDbContext(_tenant.Options);
        var orderCount = await verify.Orders.CountAsync();
        orderCount.Should().Be(0);
        var outboxCount = await verify.OutboxMessages.CountAsync();
        outboxCount.Should().Be(0);
    }

    [Fact]
    public async Task SeedAsync_TwoConsecutiveCalls_ProduceTwoDistinctOrders()
    {
        // Each seed builds a fresh channel ref so the
        // UNIQUE(channel_external_order_id) index does not block repeat
        // seeds. The clock advances per call so the suffix changes.
        await using var harness1 = BuildHarness(environmentName: "Development", advanceClock: TimeSpan.Zero);
        var firstResult = await harness1.Controller.SeedAsync(
            new SeedOrderRequest(LineCount: 2),
            idempotencyKey: null,
            CancellationToken.None);
        var firstBody = firstResult
            .Should().BeOfType<CreatedAtActionResult>()
            .Subject.Value.Should().BeOfType<OrderResponse>().Subject;

        await using var harness2 = BuildHarness(environmentName: "Development", advanceClock: TimeSpan.FromSeconds(1));
        var secondResult = await harness2.Controller.SeedAsync(
            new SeedOrderRequest(LineCount: 2),
            idempotencyKey: null,
            CancellationToken.None);
        var secondBody = secondResult
            .Should().BeOfType<CreatedAtActionResult>()
            .Subject.Value.Should().BeOfType<OrderResponse>().Subject;

        firstBody.Id.Should().NotBe(secondBody.Id);
        firstBody.ChannelExternalOrderId.Should().NotBe(secondBody.ChannelExternalOrderId);
    }

    private ControllerHarness BuildHarness(string environmentName, TimeSpan? advanceClock = null)
    {
        var services = new ServiceCollection();
        services.AddLogging(b => b.AddProvider(NullLoggerProvider.Instance));

        var rc = _tenant.BuildRequestContext();
        services.AddSingleton<IRequestContext>(rc);
        services.AddSingleton(TimeProvider.System);

        services.AddScoped<OutboundDbContext>(_ => new OutboundDbContext(_tenant.Options));
        services.AddScoped<IOrderRepository, OrderRepository>();
        services.AddScoped<IOrderTransitionRepository, OrderTransitionRepository>();
        services.AddScoped<IUnitOfWork, OutboundUnitOfWork>();
        services.AddScoped<IOutboundOutbox, OutboundOutbox>();

        services.AddMediatR(cfg => cfg.RegisterServicesFromAssembly(
            typeof(ListOrdersQuery).Assembly));

        var sp = services.BuildServiceProvider();
        var scope = sp.CreateScope();

        var clock = new FakeClock(FixedNow + (advanceClock ?? TimeSpan.Zero));

        var controller = new OrdersController(
            orderRepo: scope.ServiceProvider.GetRequiredService<IOrderRepository>(),
            uow: scope.ServiceProvider.GetRequiredService<IUnitOfWork>(),
            outbox: new OutboundOutbox(
                scope.ServiceProvider.GetRequiredService<OutboundDbContext>(),
                rc,
                clock),
            requestContext: rc,
            clock: clock,
            publishEndpoint: new NoopPublishEndpoint(),
            shippingProvider: new UnusedMockShippingProvider(),
            mediator: scope.ServiceProvider.GetRequiredService<IMediator>(),
            env: new TestHostEnvironment(environmentName));

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
        public TestHostEnvironment(string environmentName) { EnvironmentName = environmentName; }
        public string EnvironmentName { get; set; }
        public string ApplicationName { get; set; } = "ShopFlow.Outbound.Api";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider
        {
            get; set;
        } = new Microsoft.Extensions.FileProviders.NullFileProvider();
    }

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
            throw new InvalidOperationException("Seed endpoint should not call the shipping provider.");
    }
}
