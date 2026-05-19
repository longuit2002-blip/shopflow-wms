using MassTransit;
using MassTransit.Testing;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using ShopFlow.Contracts.Outbound;
using ShopFlow.Outbound.Application.Ports;
using ShopFlow.Outbound.Application.Sagas;
using ShopFlow.Outbound.Domain;
using ShopFlow.Outbound.Infrastructure;
using ShopFlow.Outbound.Infrastructure.Outbox;
using ShopFlow.Outbound.Infrastructure.Repositories;
using ShopFlow.SharedKernel.Application;
using ShopFlow.SharedKernel.Application.Ports;
using ShopFlow.SharedKernel.Domain;
using ShopFlow.SharedKernel.Infrastructure.SignalR;
using PickQueueImpl = ShopFlow.Outbound.Infrastructure.PickQueue.PickQueue;

namespace ShopFlow.Outbound.IntegrationTests.Sagas;

/// <summary>
/// Sprint-7 U6 — end-to-end integration test for the saga → observer →
/// outbox → relay-consumer → SignalR-hub chain. Validates the U6
/// architectural claim that <c>SagaTransitionedV1</c> events the saga emits
/// are surfaced as <c>"saga_transitioned"</c> hub events on the tenant
/// SignalR group.
/// </summary>
/// <remarks>
/// <para><strong>Wiring strategy.</strong> The chain has four hops:</para>
/// <list type="number">
///   <item><description>Saga's <c>RecordTransitionAsync</c> calls
///     <see cref="SagaTransitionObserver"/> which writes an audit row to
///     <c>outbound_saga_transitions</c> AND appends a
///     <see cref="SagaTransitionedV1"/> outbox row.</description></item>
///   <item><description>Outbox dispatcher polls + publishes the event onto
///     the bus.</description></item>
///   <item><description><see cref="SagaTransitionedRelayConsumer"/>
///     consumes the published event, resolves the tenant slug from the
///     control-plane catalog, builds a <see cref="SagaTransitionedPayload"/>,
///     and pushes it to the SignalR group.</description></item>
///   <item><description>SignalR delivers to every client in the
///     <c>tenant:{slug}</c> group.</description></item>
/// </list>
///
/// <para>Hops 1 and 3 are the load-bearing surfaces. Hop 2 (the dispatcher)
/// is exercised independently in the dispatcher's own integration tests; this
/// test substitutes the dispatcher with a direct <c>harness.Bus.Publish</c>
/// of <see cref="SagaTransitionedV1"/> so the chain is observable end-to-end
/// in a single MT TestHarness boot. Hop 4 (real SignalR transport) requires a
/// full <c>WebApplicationFactory&lt;Program&gt;</c>; we substitute a mocked
/// <see cref="IHubContext{THub}"/> and assert <c>SendCoreAsync</c> received
/// the expected call.</para>
///
/// <para>Two-phase assertion: first the saga path writes the audit row
/// (Phase A — saga side), then the relay consumer receives the published
/// event and fans it out to the hub (Phase B — relay side).</para>
/// </remarks>
[Collection(OutboundTenantCollection.Name)]
[Trait("Category", "Integration")]
public sealed class SagaTransitionsEndToEndSignalRTests : IAsyncLifetime
{
    private const string Slug = "saga-relay-e2e";

    private readonly OutboundTenantFixture _fx;
    private ProvisionedOutboundTenant _tenant = default!;

    public SagaTransitionsEndToEndSignalRTests(OutboundTenantFixture fx)
    {
        _fx = fx;
    }

    public async Task InitializeAsync()
    {
        _tenant = await _fx.ProvisionTenantAsync(Slug);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private (
        ServiceProvider sp,
        ITestHarness harness,
        IClientProxy groupProxy,
        IHubClients clients
    ) BuildHostWithRelay(out ITenantCatalog catalog)
    {
        var services = new ServiceCollection();
        services.AddLogging(b => b.AddProvider(NullLoggerProvider.Instance));

        var rc = _tenant.BuildRequestContext();
        services.AddSingleton<IRequestContext>(rc);
        services.AddSingleton(TimeProvider.System);

        services.AddScoped<OutboundDbContext>(sp =>
        {
            var ctx = sp.GetRequiredService<IRequestContext>();
            var options = new DbContextOptionsBuilder<OutboundDbContext>()
                .UseNpgsql(ctx.DbConnectionString)
                .Options;
            return new OutboundDbContext(options);
        });
        services.AddScoped<IOrderTransitionRepository, OrderTransitionRepository>();
        services.AddScoped<IOutboundOutbox, OutboundOutbox>();
        services.AddScoped<SagaTransitionObserver>();
        services.AddSingleton<IPickQueue, PickQueueImpl>();

        // U6 relay dependencies — mocked IHubContext (real SignalR would
        // require WebApplicationFactory) + a controlled ITenantCatalog that
        // resolves the provisioned tenant id back to a known slug.
        var clients = Substitute.For<IHubClients>();
        var groupProxy = Substitute.For<IClientProxy>();
        var hub = Substitute.For<IHubContext<TenantHub>>();
        hub.Clients.Returns(clients);
        clients.Group(Arg.Any<string>()).Returns(groupProxy);
        services.AddSingleton(hub);

        var resolvedCatalog = Substitute.For<ITenantCatalog>();
        var tenantInfo = new TenantInfo(
            Id: _tenant.Info.Id,
            Slug: Slug,
            DbName: _tenant.Info.DbName,
            DbConnectionString: _tenant.Info.DbConnectionString,
            Region: "ap-southeast-1",
            Tier: "free",
            Status: TenantStatus.Ready
        );
        resolvedCatalog
            .LookupByIdAsync(_tenant.Info.Id, Arg.Any<CancellationToken>())
            .Returns(tenantInfo);
        services.AddSingleton(resolvedCatalog);

        services.AddMassTransitTestHarness(cfg =>
        {
            cfg.AddSagaStateMachine<FulfillmentSaga, FulfillmentSagaState>()
                .InMemoryRepository();

            // The U6 relay consumer registered alongside the saga so a
            // single in-memory bus carries both the saga's events and the
            // relay's subscription.
            cfg.AddConsumer<SagaTransitionedRelayConsumer>();
        });

        var sp = services.BuildServiceProvider(true);
        var harness = sp.GetRequiredService<ITestHarness>();
        catalog = resolvedCatalog;
        return (sp, harness, groupProxy, clients);
    }

    [Fact]
    public async Task SagaTransition_WritesAuditRow_AndRelayPushesHubEvent()
    {
        var (sp, harness, groupProxy, clients) = BuildHostWithRelay(out _);
        await using var _disposable = sp;
        await harness.Start();

        var orderId = Guid.NewGuid();

        // Phase A — saga side. Publish OrderPlacedV1 → saga transitions
        // Initial → AwaitingReservation → audit row written.
        await harness.Bus.Publish(
            new OrderPlacedV1(
                OrderId: orderId,
                TenantId: _tenant.Info.Id,
                ChannelExternalOrderId: "ext-saga-relay-1",
                ShippingProfile: "standard",
                Lines: new[] { new OrderPlacedLineV1("L1", "SKU-A", 1, 100) },
                OccurredAt: DateTime.UtcNow
            )
        );

        (await harness.Consumed.Any<OrderPlacedV1>()).Should().BeTrue();

        // Verify the saga's observer wrote one outbound_saga_transitions row.
        OrderTransition row;
        await using (var db = new OutboundDbContext(_tenant.Options))
        {
            var rows = await db
                .OrderTransitions.Where(t => t.OrderId == orderId)
                .OrderBy(t => t.OccurredAt)
                .ToListAsync();
            rows.Should().HaveCount(1);
            row = rows[0];
            row.FromState.Should().Be("Initial");
            row.ToState.Should().Be("AwaitingReservation");
            row.EventType.Should().Be(nameof(OrderPlacedV1));
        }

        // Phase B — relay side. Simulate the outbox dispatcher publishing
        // the SagaTransitionedV1 the observer appended to the outbox table
        // (the dispatcher itself is exercised in its own integration tests).
        // The relay consumer subscribes via cfg.AddConsumer<>() above and
        // should receive the message + push to the hub.
        var integrationEvent = new SagaTransitionedV1(
            TenantId: _tenant.Info.Id,
            OrderId: orderId,
            FromState: row.FromState,
            ToState: row.ToState,
            OccurredAt: row.OccurredAt,
            EventType: row.EventType,
            CorrelationId: row.CorrelationId
        );
        await harness.Bus.Publish(integrationEvent);

        var relayHarness = harness.GetConsumerHarness<SagaTransitionedRelayConsumer>();
        (await relayHarness.Consumed.Any<SagaTransitionedV1>()).Should().BeTrue();

        // Assert — relay landed in the right tenant group + payload preserved.
        clients.Received(1).Group($"tenant:{Slug}");
        await groupProxy
            .Received(1)
            .SendCoreAsync(
                SagaTransitionedRelayConsumer.HubEventName,
                Arg.Is<object?[]>(args =>
                    args.Length == 1
                    && args[0] is SagaTransitionedPayload
                    && ((SagaTransitionedPayload)args[0]!).TenantId == _tenant.Info.Id
                    && ((SagaTransitionedPayload)args[0]!).OrderId == orderId
                    && ((SagaTransitionedPayload)args[0]!).FromState == "Initial"
                    && ((SagaTransitionedPayload)args[0]!).ToState == "AwaitingReservation"
                    && ((SagaTransitionedPayload)args[0]!).EventType == nameof(OrderPlacedV1)
                ),
                Arg.Any<CancellationToken>()
            );

        await harness.Stop();
    }
}
