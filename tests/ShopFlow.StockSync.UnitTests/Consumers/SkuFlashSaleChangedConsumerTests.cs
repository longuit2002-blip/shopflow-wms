using MassTransit;
using MassTransit.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using ShopFlow.Contracts.Inventory;
using ShopFlow.StockSync.Application.Consumers;
using ShopFlow.StockSync.Application.Ports;

namespace ShopFlow.StockSync.UnitTests.Consumers;

/// <summary>
/// Sprint-7.5 U5 — <c>SkuFlashSaleChangedConsumer</c> behavioural
/// contract. Pins the delegate-to-port shape (the OccurredAt guard +
/// 23505 idempotency live inside <c>ISkuFlagRepository.ApplyEventAsync</c>
/// so the consumer itself stays thin).
/// </summary>
public sealed class SkuFlashSaleChangedConsumerTests
{
    private static readonly Guid TenantA = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly DateTime Now = new(2026, 5, 19, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task Consume_DelegatesToApplyEventAsyncWithMessagePayload()
    {
        var repo = Substitute.For<ISkuFlagRepository>();
        repo.ApplyEventAsync(
                Arg.Any<Guid>(),
                Arg.Any<string>(),
                Arg.Any<bool>(),
                Arg.Any<DateTime>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(true);

        var harness = await StartHarnessAsync(repo);
        try
        {
            await harness.Bus.Publish(new SkuFlashSaleChangedV1(TenantA, "SKU-A", true, Now));

            var consumed = harness.GetConsumerHarness<SkuFlashSaleChangedConsumer>();
            (await consumed.Consumed.Any<SkuFlashSaleChangedV1>()).Should().BeTrue();

            await repo.Received(1)
                .ApplyEventAsync(TenantA, "SKU-A", true, Now, Arg.Any<CancellationToken>());
        }
        finally
        {
            await harness.Stop();
        }
    }

    [Fact]
    public async Task Consume_StaleEventRejectedByPort_DoesNotThrow()
    {
        var repo = Substitute.For<ISkuFlagRepository>();
        // Port returns false = stale write dropped. Consumer must complete
        // successfully (the event is acked from MT's perspective).
        repo.ApplyEventAsync(
                Arg.Any<Guid>(),
                Arg.Any<string>(),
                Arg.Any<bool>(),
                Arg.Any<DateTime>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(false);

        var harness = await StartHarnessAsync(repo);
        try
        {
            await harness.Bus.Publish(
                new SkuFlashSaleChangedV1(TenantA, "SKU-A", true, Now.AddMinutes(-5))
            );

            var consumed = harness.GetConsumerHarness<SkuFlashSaleChangedConsumer>();
            (await consumed.Consumed.Any<SkuFlashSaleChangedV1>()).Should().BeTrue();

            // No exception thrown by the consumer; MT considers the
            // message successfully handled.
            var faulted = harness
                .Consumed.Select<SkuFlashSaleChangedV1>()
                .Any(ctx => ctx.Exception is not null);
            faulted.Should().BeFalse();
        }
        finally
        {
            await harness.Stop();
        }
    }

    private static async Task<ITestHarness> StartHarnessAsync(ISkuFlagRepository repo)
    {
        var services = new ServiceCollection();
        services.AddSingleton(repo);
        services.AddSingleton<ILogger<SkuFlashSaleChangedConsumer>>(
            NullLogger<SkuFlashSaleChangedConsumer>.Instance
        );
        services.AddMassTransitTestHarness(cfg =>
        {
            cfg.AddConsumer<SkuFlashSaleChangedConsumer>();
        });
        var provider = services.BuildServiceProvider(validateScopes: true);
        var harness = provider.GetRequiredService<ITestHarness>();
        await harness.Start();
        return harness;
    }
}
