using MassTransit;
using MassTransit.Testing;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using ShopFlow.Contracts.Outbound;
using ShopFlow.SharedKernel.Application.Ports;
using ShopFlow.SharedKernel.Domain;
using ShopFlow.SharedKernel.Infrastructure.SignalR;

namespace ShopFlow.SharedKernel.UnitTests.SignalR;

/// <summary>
/// Sprint-7 plan U6 — covers <see cref="SagaTransitionedRelayConsumer"/>:
/// happy path, unknown tenant graceful drop, hub failure rethrow. Mirrors
/// <see cref="StockChangedRelayConsumerTests"/> with the saga-specific
/// payload assertions (7 fields, <c>"saga_transitioned"</c> event name).
/// </summary>
public sealed class SagaTransitionedRelayConsumerTests
{
    private const string Slug = "yensaokhanhhoa";
    private static readonly Guid TenantId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid OrderId = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly DateTime OccurredAt = new(2026, 5, 19, 11, 0, 0, DateTimeKind.Utc);
    private const string CorrelationId = "abc-correlation-from-saga";

    private static TenantInfo SampleTenant(string slug = Slug) =>
        new(
            Id: TenantId,
            Slug: slug,
            DbName: $"shopflow_t_{slug}",
            DbConnectionString: $"Host=pgbouncer;Database=shopflow_t_{slug};Username=app;Password=test",
            Region: "ap-southeast-1",
            Tier: "free",
            Status: TenantStatus.Ready
        );

    private static SagaTransitionedV1 NewEvent() =>
        new(
            TenantId: TenantId,
            OrderId: OrderId,
            FromState: "AwaitingReservation",
            ToState: "Reserved",
            OccurredAt: OccurredAt,
            EventType: "StockReservedV1",
            CorrelationId: CorrelationId
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
    public async Task Consume_HappyPath_PushesSagaTransitionedWithAllSevenFields()
    {
        // Arrange
        var msg = NewEvent();

        var catalog = Substitute.For<ITenantCatalog>();
        catalog.LookupByIdAsync(TenantId, Arg.Any<CancellationToken>()).Returns(SampleTenant());

        var (hub, clients, groupProxy) = BuildHubContext();

        var services = new ServiceCollection();
        services.AddSingleton(hub);
        services.AddSingleton(catalog);
        services.AddLogging(b => b.AddProvider(NullLoggerProvider.Instance));
        services.AddMassTransitTestHarness(cfg => cfg.AddConsumer<SagaTransitionedRelayConsumer>());

        await using var sp = services.BuildServiceProvider(true);
        var harness = sp.GetRequiredService<ITestHarness>();
        await harness.Start();

        // Act
        await harness.Bus.Publish(msg);

        var consumerHarness = harness.GetConsumerHarness<SagaTransitionedRelayConsumer>();
        (await consumerHarness.Consumed.Any<SagaTransitionedV1>()).Should().BeTrue();

        // Assert — group resolved by slug; payload preserves all 7 fields verbatim.
        clients.Received(1).Group($"tenant:{Slug}");
        await groupProxy
            .Received(1)
            .SendCoreAsync(
                SagaTransitionedRelayConsumer.HubEventName,
                Arg.Is<object?[]>(args =>
                    args.Length == 1
                    && args[0] is SagaTransitionedPayload
                    && ((SagaTransitionedPayload)args[0]!).TenantId == TenantId
                    && ((SagaTransitionedPayload)args[0]!).OrderId == OrderId
                    && ((SagaTransitionedPayload)args[0]!).FromState == "AwaitingReservation"
                    && ((SagaTransitionedPayload)args[0]!).ToState == "Reserved"
                    && ((SagaTransitionedPayload)args[0]!).OccurredAt == OccurredAt
                    && ((SagaTransitionedPayload)args[0]!).EventType == "StockReservedV1"
                    && ((SagaTransitionedPayload)args[0]!).CorrelationId == CorrelationId
                ),
                Arg.Any<CancellationToken>()
            );

        await harness.Stop();
    }

    [Fact]
    public async Task Consume_HubEventNameIsSagaTransitioned()
    {
        // Belt-and-braces: the const value is the contract surface for the
        // frontend useSignalR hook (Sprint-7 U7). Lock it down independently
        // so a rename in the relay consumer immediately fails this test.
        SagaTransitionedRelayConsumer.HubEventName.Should().Be("saga_transitioned");
        await Task.CompletedTask;
    }

    [Fact]
    public async Task Consume_UnknownTenant_LogsAndReturnsCleanly_DoesNotSend()
    {
        // Arrange
        var msg = NewEvent();

        var catalog = Substitute.For<ITenantCatalog>();
        catalog.LookupByIdAsync(TenantId, Arg.Any<CancellationToken>()).Returns((TenantInfo?)null);

        var (hub, clients, groupProxy) = BuildHubContext();

        var services = new ServiceCollection();
        services.AddSingleton(hub);
        services.AddSingleton(catalog);
        services.AddLogging(b => b.AddProvider(NullLoggerProvider.Instance));
        services.AddMassTransitTestHarness(cfg => cfg.AddConsumer<SagaTransitionedRelayConsumer>());

        await using var sp = services.BuildServiceProvider(true);
        var harness = sp.GetRequiredService<ITestHarness>();
        await harness.Start();

        // Act
        await harness.Bus.Publish(msg);

        var consumerHarness = harness.GetConsumerHarness<SagaTransitionedRelayConsumer>();
        (await consumerHarness.Consumed.Any<SagaTransitionedV1>()).Should().BeTrue();

        // Assert
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
        var msg = NewEvent();

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

        var consumer = new SagaTransitionedRelayConsumer(
            hub,
            catalog,
            NullLogger<SagaTransitionedRelayConsumer>.Instance
        );

        var ctx = Substitute.For<ConsumeContext<SagaTransitionedV1>>();
        ctx.Message.Returns(msg);
        ctx.CancellationToken.Returns(CancellationToken.None);

        // Act
        Func<Task> act = () => consumer.Consume(ctx);

        // Assert
        await act.Should()
            .ThrowAsync<InvalidOperationException>()
            .WithMessage("hub transport down");
    }
}
