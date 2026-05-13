using MassTransit;
using MassTransit.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using ShopFlow.Contracts.Channel;
using ShopFlow.Outbound.Application.Consumers;
using ShopFlow.Outbound.Application.Ports;
using ShopFlow.Outbound.Domain;
using ShopFlow.SharedKernel.Application;

namespace ShopFlow.Outbound.UnitTests.Consumers;

/// <summary>
/// Sprint-4 plan U8 — OrderImportedConsumer coverage. Confirms the
/// happy-path Order creation + outbox-append + duplicate idempotency
/// short-circuit. Integration with the real EF + saga path runs in CI.
/// </summary>
public sealed class OrderImportedConsumerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid ChannelId = Guid.NewGuid();
    private static readonly DateTime Now = new(2026, 5, 13, 10, 0, 0, DateTimeKind.Utc);

    private static OrderImportedV1 NewMessage(string externalOrderId = "ext-001") =>
        new(
            OrderId: Guid.NewGuid(),
            TenantId: TenantId,
            ChannelId: ChannelId,
            ChannelExternalOrderId: externalOrderId,
            ShippingProfile: "STANDARD",
            Lines: new[] { new OrderImportedLineV1("SKU-1", 2) },
            OccurredAt: Now
        );

    [Fact]
    public async Task Consume_NewMessage_CreatesOrder_AndAppendsOutbox()
    {
        var orderRepo = Substitute.For<IOrderRepository>();
        var uow = Substitute.For<IUnitOfWork>();
        var outbox = Substitute.For<IOutboundOutbox>();
        var requestContext = Substitute.For<IRequestContext>();
        requestContext.TenantId.Returns(TenantId);
        var clock = TimeProvider.System;

        orderRepo.FindByExternalIdAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns((Order?)null);

        var services = new ServiceCollection();
        services.AddSingleton(orderRepo);
        services.AddSingleton(uow);
        services.AddSingleton(outbox);
        services.AddSingleton(requestContext);
        services.AddSingleton(clock);
        services.AddLogging(b => b.AddProvider(NullLoggerProvider.Instance));
        services.AddMassTransitTestHarness(cfg => cfg.AddConsumer<OrderImportedConsumer>());

        await using var sp = services.BuildServiceProvider(true);
        var harness = sp.GetRequiredService<ITestHarness>();
        await harness.Start();

        await harness.Bus.Publish(NewMessage());

        var consumerHarness = harness.GetConsumerHarness<OrderImportedConsumer>();
        (await consumerHarness.Consumed.Any<OrderImportedV1>()).Should().BeTrue();

        await orderRepo.Received(1).AddAsync(Arg.Any<Order>(), Arg.Any<CancellationToken>());
        await outbox.Received(1).AppendAsync(
            Arg.Any<string>(), Arg.Any<object>(), Arg.Any<CancellationToken>()
        );
        await uow.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());

        await harness.Stop();
    }

    [Fact]
    public async Task Consume_DuplicateMessage_ShortCircuits()
    {
        var orderRepo = Substitute.For<IOrderRepository>();
        var uow = Substitute.For<IUnitOfWork>();
        var outbox = Substitute.For<IOutboundOutbox>();
        var requestContext = Substitute.For<IRequestContext>();
        requestContext.TenantId.Returns(TenantId);

        // Pre-seed: an existing Order for the same channel_external_order_id.
        var existing = Order
            .Create("ext-001", "STANDARD", new[] { ("SKU-1", 2, (int?)null) })
            .Value!;
        orderRepo
            .FindByExternalIdAsync("ext-001", Arg.Any<CancellationToken>())
            .Returns(existing);

        var services = new ServiceCollection();
        services.AddSingleton(orderRepo);
        services.AddSingleton(uow);
        services.AddSingleton(outbox);
        services.AddSingleton(requestContext);
        services.AddSingleton(TimeProvider.System);
        services.AddLogging(b => b.AddProvider(NullLoggerProvider.Instance));
        services.AddMassTransitTestHarness(cfg => cfg.AddConsumer<OrderImportedConsumer>());

        await using var sp = services.BuildServiceProvider(true);
        var harness = sp.GetRequiredService<ITestHarness>();
        await harness.Start();

        await harness.Bus.Publish(NewMessage("ext-001"));

        var consumerHarness = harness.GetConsumerHarness<OrderImportedConsumer>();
        (await consumerHarness.Consumed.Any<OrderImportedV1>()).Should().BeTrue();

        await orderRepo.DidNotReceive().AddAsync(Arg.Any<Order>(), Arg.Any<CancellationToken>());
        await outbox.DidNotReceive().AppendAsync(
            Arg.Any<string>(), Arg.Any<object>(), Arg.Any<CancellationToken>()
        );
        await uow.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());

        await harness.Stop();
    }

    [Fact]
    public async Task Consume_EmptyLines_SkipsCreate()
    {
        var orderRepo = Substitute.For<IOrderRepository>();
        var uow = Substitute.For<IUnitOfWork>();
        var outbox = Substitute.For<IOutboundOutbox>();
        var requestContext = Substitute.For<IRequestContext>();
        requestContext.TenantId.Returns(TenantId);

        var services = new ServiceCollection();
        services.AddSingleton(orderRepo);
        services.AddSingleton(uow);
        services.AddSingleton(outbox);
        services.AddSingleton(requestContext);
        services.AddSingleton(TimeProvider.System);
        services.AddLogging(b => b.AddProvider(NullLoggerProvider.Instance));
        services.AddMassTransitTestHarness(cfg => cfg.AddConsumer<OrderImportedConsumer>());

        await using var sp = services.BuildServiceProvider(true);
        var harness = sp.GetRequiredService<ITestHarness>();
        await harness.Start();

        var emptyLines = new OrderImportedV1(
            Guid.NewGuid(),
            TenantId,
            ChannelId,
            "ext-empty",
            "STANDARD",
            Array.Empty<OrderImportedLineV1>(),
            Now
        );
        await harness.Bus.Publish(emptyLines);

        var consumerHarness = harness.GetConsumerHarness<OrderImportedConsumer>();
        (await consumerHarness.Consumed.Any<OrderImportedV1>()).Should().BeTrue();

        await orderRepo.DidNotReceive().AddAsync(Arg.Any<Order>(), Arg.Any<CancellationToken>());
        await outbox.DidNotReceive().AppendAsync(
            Arg.Any<string>(), Arg.Any<object>(), Arg.Any<CancellationToken>()
        );

        await harness.Stop();
    }
}
