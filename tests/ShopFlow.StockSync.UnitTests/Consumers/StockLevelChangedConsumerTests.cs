using MassTransit;
using MassTransit.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using ShopFlow.Contracts.Inventory;
using ShopFlow.StockSync.Application.Coalescing;
using ShopFlow.StockSync.Application.Consumers;
using ShopFlow.StockSync.Application.Ports;

namespace ShopFlow.StockSync.UnitTests.Consumers;

/// <summary>
/// Sprint-5 plan U3 — <c>StockLevelChangedConsumer</c> behavioural
/// contract. Covers per-channel fan-out (R5 mirror-all), the
/// <c>is_flash_sale</c> stamp (R10), the no-active-channels guard,
/// and MassTransit redelivery idempotency.
/// </summary>
public sealed class StockLevelChangedConsumerTests
{
    private static readonly Guid TenantA = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly DateTime Now = new(2026, 5, 16, 10, 0, 0, DateTimeKind.Utc);

    private static StockLevelChangedV1 NewMessage(int available = 7, string sku = "SKU-X") =>
        new(TenantA, sku, available, Now);

    [Fact]
    public async Task Consume_FansOutOnePerActiveChannel()
    {
        var buffer = new CoalescingBuffer();
        var channelLookup = Substitute.For<IChannelLookupPort>();
        channelLookup.GetActiveChannelsAsync(TenantA, Arg.Any<CancellationToken>())
            .Returns(new[] { "shopee", "lazada" });
        var skuFlagRepo = Substitute.For<ISkuFlagRepository>();
        skuFlagRepo.IsFlashSaleAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(false);

        var harness = await StartHarnessAsync(buffer, channelLookup, skuFlagRepo);
        try
        {
            await harness.Bus.Publish(NewMessage());
            var consumerHarness = harness.GetConsumerHarness<StockLevelChangedConsumer>();
            (await consumerHarness.Consumed.Any<StockLevelChangedV1>()).Should().BeTrue();

            var snapshot = buffer.SnapshotAndClear();
            snapshot.Should().HaveCount(2);
            snapshot.Select(kv => kv.Key.ChannelType)
                .Should().BeEquivalentTo(new[] { "shopee", "lazada" });
            snapshot.Should().OnlyContain(kv => kv.Value.AvailableToSell == 7);
        }
        finally
        {
            await harness.Stop();
        }
    }

    [Fact]
    public async Task Consume_StampsIsFlashSale_FromRepository()
    {
        var buffer = new CoalescingBuffer();
        var channelLookup = Substitute.For<IChannelLookupPort>();
        channelLookup.GetActiveChannelsAsync(TenantA, Arg.Any<CancellationToken>())
            .Returns(new[] { "shopee" });
        var skuFlagRepo = Substitute.For<ISkuFlagRepository>();
        skuFlagRepo.IsFlashSaleAsync(TenantA, "SKU-X", Arg.Any<CancellationToken>())
            .Returns(true);

        var harness = await StartHarnessAsync(buffer, channelLookup, skuFlagRepo);
        try
        {
            await harness.Bus.Publish(NewMessage(sku: "SKU-X"));
            (await harness.GetConsumerHarness<StockLevelChangedConsumer>()
                .Consumed.Any<StockLevelChangedV1>()).Should().BeTrue();

            var snapshot = buffer.SnapshotAndClear();
            snapshot.Should().HaveCount(1);
            snapshot[0].Value.IsFlashSale.Should().BeTrue();
        }
        finally
        {
            await harness.Stop();
        }
    }

    [Fact]
    public async Task Consume_NoActiveChannels_SkipsBufferWrite()
    {
        var buffer = new CoalescingBuffer();
        var channelLookup = Substitute.For<IChannelLookupPort>();
        channelLookup.GetActiveChannelsAsync(TenantA, Arg.Any<CancellationToken>())
            .Returns(Array.Empty<string>());
        var skuFlagRepo = Substitute.For<ISkuFlagRepository>();

        var harness = await StartHarnessAsync(buffer, channelLookup, skuFlagRepo);
        try
        {
            await harness.Bus.Publish(NewMessage());
            (await harness.GetConsumerHarness<StockLevelChangedConsumer>()
                .Consumed.Any<StockLevelChangedV1>()).Should().BeTrue();

            buffer.Count.Should().Be(0);
            await skuFlagRepo.DidNotReceive().IsFlashSaleAsync(
                Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        }
        finally
        {
            await harness.Stop();
        }
    }

    [Fact]
    public async Task Consume_RedeliveredMessage_LeavesBufferStateUnchanged()
    {
        var buffer = new CoalescingBuffer();
        var channelLookup = Substitute.For<IChannelLookupPort>();
        channelLookup.GetActiveChannelsAsync(TenantA, Arg.Any<CancellationToken>())
            .Returns(new[] { "shopee" });
        var skuFlagRepo = Substitute.For<ISkuFlagRepository>();
        skuFlagRepo.IsFlashSaleAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(false);

        var harness = await StartHarnessAsync(buffer, channelLookup, skuFlagRepo);
        try
        {
            var msg = NewMessage(available: 5);

            // Two publishes simulate at-least-once redelivery. The
            // consumer's Upsert is last-by-ObservedAt — same OccurredAt
            // means the second write is a no-op snapshot value.
            await harness.Bus.Publish(msg);
            await harness.Bus.Publish(msg);

            var consumerHarness = harness.GetConsumerHarness<StockLevelChangedConsumer>();
            (await consumerHarness.Consumed.SelectAsync<StockLevelChangedV1>().Take(2).Count())
                .Should().Be(2);

            var snapshot = buffer.SnapshotAndClear();
            snapshot.Should().HaveCount(1, "redelivery collapses onto the same coalesce key");
            snapshot[0].Value.AvailableToSell.Should().Be(5);
            snapshot[0].Value.ObservedAt.Should().Be(Now);
        }
        finally
        {
            await harness.Stop();
        }
    }

    [Fact]
    public async Task Consume_MultipleMessages_SameKeyDifferentObservedAt_KeepsLatest()
    {
        var buffer = new CoalescingBuffer();
        var channelLookup = Substitute.For<IChannelLookupPort>();
        channelLookup.GetActiveChannelsAsync(TenantA, Arg.Any<CancellationToken>())
            .Returns(new[] { "shopee" });
        var skuFlagRepo = Substitute.For<ISkuFlagRepository>();
        skuFlagRepo.IsFlashSaleAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(false);

        var harness = await StartHarnessAsync(buffer, channelLookup, skuFlagRepo);
        try
        {
            await harness.Bus.Publish(new StockLevelChangedV1(TenantA, "SKU-X", 10, Now));
            await harness.Bus.Publish(new StockLevelChangedV1(TenantA, "SKU-X", 3, Now.AddSeconds(1)));

            var consumerHarness = harness.GetConsumerHarness<StockLevelChangedConsumer>();
            (await consumerHarness.Consumed.SelectAsync<StockLevelChangedV1>().Take(2).Count())
                .Should().Be(2);

            var snapshot = buffer.SnapshotAndClear();
            snapshot.Should().HaveCount(1);
            snapshot[0].Value.AvailableToSell.Should().Be(3, "last-by-ObservedAt wins");
        }
        finally
        {
            await harness.Stop();
        }
    }

    private static async Task<ITestHarness> StartHarnessAsync(
        ICoalescingBuffer buffer,
        IChannelLookupPort channelLookup,
        ISkuFlagRepository skuFlagRepo)
    {
        var services = new ServiceCollection();
        services.AddSingleton(buffer);
        services.AddSingleton(channelLookup);
        services.AddSingleton(skuFlagRepo);
        services.AddLogging(b => b.AddProvider(NullLoggerProvider.Instance));
        services.AddMassTransitTestHarness(cfg =>
        {
            cfg.AddConsumer<StockLevelChangedConsumer>();
        });

        var sp = services.BuildServiceProvider(true);
        var harness = sp.GetRequiredService<ITestHarness>();
        await harness.Start();
        return harness;
    }
}
