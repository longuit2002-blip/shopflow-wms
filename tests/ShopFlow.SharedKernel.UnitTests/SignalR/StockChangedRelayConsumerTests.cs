using MassTransit;
using MassTransit.Testing;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using ShopFlow.Contracts.Inventory;
using ShopFlow.SharedKernel.Application.Ports;
using ShopFlow.SharedKernel.Domain;
using ShopFlow.SharedKernel.Infrastructure.SignalR;

namespace ShopFlow.SharedKernel.UnitTests.SignalR;

/// <summary>
/// Sprint-7 plan U6 — covers <see cref="StockChangedRelayConsumer"/>:
/// happy-path SendAsync to the tenant group, unknown-tenant graceful
/// drop, and hub-failure rethrow (lets MT retry policy take over).
/// </summary>
/// <remarks>
/// SignalR's <c>SendAsync</c> is an extension on <see cref="IClientProxy"/>
/// that delegates to <see cref="IClientProxy.SendCoreAsync"/>. The mock
/// chain mirrors that internal seam: <c>IHubContext.Clients.Group(name)</c>
/// returns <c>IClientProxy</c>, and the assertion checks
/// <c>SendCoreAsync</c> received the expected method + args.
/// </remarks>
public sealed class StockChangedRelayConsumerTests
{
    private const string Slug = "yensaokhanhhoa";
    private static readonly Guid TenantId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly DateTime OccurredAt = new(2026, 5, 19, 10, 0, 0, DateTimeKind.Utc);

    private static TenantInfo SampleTenant(
        TenantStatus status = TenantStatus.Ready,
        string slug = Slug
    ) =>
        new(
            Id: TenantId,
            Slug: slug,
            DbName: $"shopflow_t_{slug}",
            DbConnectionString: $"Host=pgbouncer;Database=shopflow_t_{slug};Username=app;Password=test",
            Region: "ap-southeast-1",
            Tier: "free",
            Status: status
        );

    private static (
        IHubContext<TenantHub> hub,
        IHubClients clients,
        IClientProxy groupProxy
    ) BuildHubContext()
    {
        var hub = Substitute.For<IHubContext<TenantHub>>();
        var clients = Substitute.For<IHubClients>();
        var groupProxy = Substitute.For<IClientProxy>();
        hub.Clients.Returns(clients);
        clients.Group(Arg.Any<string>()).Returns(groupProxy);
        return (hub, clients, groupProxy);
    }

    [Fact]
    public async Task Consume_HappyPath_SendsToTenantGroup()
    {
        // Arrange
        var msg = new StockLevelChangedV1(
            TenantId: TenantId,
            Sku: "SKU-A",
            AvailableToSell: 42,
            OccurredAt: OccurredAt
        );

        var catalog = Substitute.For<ITenantCatalog>();
        catalog.LookupByIdAsync(TenantId, Arg.Any<CancellationToken>()).Returns(SampleTenant());

        var (hub, clients, groupProxy) = BuildHubContext();

        var services = new ServiceCollection();
        services.AddSingleton(hub);
        services.AddSingleton(catalog);
        services.AddLogging(b => b.AddProvider(NullLoggerProvider.Instance));
        services.AddMassTransitTestHarness(cfg => cfg.AddConsumer<StockChangedRelayConsumer>());

        await using var sp = services.BuildServiceProvider(true);
        var harness = sp.GetRequiredService<ITestHarness>();
        await harness.Start();

        // Act
        await harness.Bus.Publish(msg);

        var consumerHarness = harness.GetConsumerHarness<StockChangedRelayConsumer>();
        (await consumerHarness.Consumed.Any<StockLevelChangedV1>()).Should().BeTrue();

        // Assert — group resolved by slug; SendCoreAsync got the right event name + payload.
        clients.Received(1).Group($"tenant:{Slug}");
        await groupProxy
            .Received(1)
            .SendCoreAsync(
                StockChangedRelayConsumer.HubEventName,
                Arg.Is<object?[]>(args =>
                    args.Length == 1
                    && args[0] is StockChangedPayload
                    && ((StockChangedPayload)args[0]!).TenantId == TenantId
                    && ((StockChangedPayload)args[0]!).Sku == "SKU-A"
                    && ((StockChangedPayload)args[0]!).AvailableToSell == 42
                    && ((StockChangedPayload)args[0]!).OccurredAt == OccurredAt
                    && !string.IsNullOrWhiteSpace(((StockChangedPayload)args[0]!).CorrelationId)
                ),
                Arg.Any<CancellationToken>()
            );

        await harness.Stop();
    }

    [Fact]
    public async Task Consume_UnknownTenant_LogsAndReturnsCleanly_DoesNotSend()
    {
        // Arrange
        var msg = new StockLevelChangedV1(
            TenantId: TenantId,
            Sku: "SKU-A",
            AvailableToSell: 7,
            OccurredAt: OccurredAt
        );

        var catalog = Substitute.For<ITenantCatalog>();
        catalog.LookupByIdAsync(TenantId, Arg.Any<CancellationToken>()).Returns((TenantInfo?)null);

        var (hub, clients, groupProxy) = BuildHubContext();

        var services = new ServiceCollection();
        services.AddSingleton(hub);
        services.AddSingleton(catalog);
        services.AddLogging(b => b.AddProvider(NullLoggerProvider.Instance));
        services.AddMassTransitTestHarness(cfg => cfg.AddConsumer<StockChangedRelayConsumer>());

        await using var sp = services.BuildServiceProvider(true);
        var harness = sp.GetRequiredService<ITestHarness>();
        await harness.Start();

        // Act
        await harness.Bus.Publish(msg);

        var consumerHarness = harness.GetConsumerHarness<StockChangedRelayConsumer>();
        // Consumed cleanly = no exception = no MT retry = harness treats it as Consumed.
        (await consumerHarness.Consumed.Any<StockLevelChangedV1>())
            .Should()
            .BeTrue();

        // Assert — catalog miss short-circuits; no SignalR send happens, no DLQ.
        clients.DidNotReceive().Group(Arg.Any<string>());
        await groupProxy
            .DidNotReceive()
            .SendCoreAsync(Arg.Any<string>(), Arg.Any<object?[]>(), Arg.Any<CancellationToken>());

        await harness.Stop();
    }

    [Fact]
    public async Task Consume_HubSendThrows_BubblesForMassTransitRetry()
    {
        // Arrange
        var msg = new StockLevelChangedV1(
            TenantId: TenantId,
            Sku: "SKU-A",
            AvailableToSell: 11,
            OccurredAt: OccurredAt
        );

        var catalog = Substitute.For<ITenantCatalog>();
        catalog.LookupByIdAsync(TenantId, Arg.Any<CancellationToken>()).Returns(SampleTenant());

        var hub = Substitute.For<IHubContext<TenantHub>>();
        var clients = Substitute.For<IHubClients>();
        var groupProxy = Substitute.For<IClientProxy>();
        hub.Clients.Returns(clients);
        clients.Group(Arg.Any<string>()).Returns(groupProxy);
        groupProxy
            .SendCoreAsync(Arg.Any<string>(), Arg.Any<object?[]>(), Arg.Any<CancellationToken>())
            .Throws(new InvalidOperationException("hub transport down"));

        // Direct-call mode: instantiate the consumer + a substituted
        // ConsumeContext so we can observe the throw deterministically.
        // MT's harness would surface the throw via Faulted; the direct
        // path is cleaner for the rethrow assertion.
        var consumer = new StockChangedRelayConsumer(
            hub,
            catalog,
            NullLogger<StockChangedRelayConsumer>.Instance
        );

        var ctx = Substitute.For<ConsumeContext<StockLevelChangedV1>>();
        ctx.Message.Returns(msg);
        ctx.CancellationToken.Returns(CancellationToken.None);

        // Act
        Func<Task> act = () => consumer.Consume(ctx);

        // Assert — the throw bubbles, letting MT's pipeline apply retry.
        await act.Should()
            .ThrowAsync<InvalidOperationException>()
            .WithMessage("hub transport down");
    }
}
