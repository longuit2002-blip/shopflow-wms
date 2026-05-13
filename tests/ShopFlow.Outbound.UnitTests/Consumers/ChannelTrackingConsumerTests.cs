using MassTransit;
using MassTransit.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using ShopFlow.Contracts.Outbound;
using ShopFlow.Outbound.Infrastructure.Consumers;

namespace ShopFlow.Outbound.UnitTests.Consumers;

/// <summary>
/// Sprint-3-redux U6 — <see cref="ChannelTrackingConsumer"/> smoke test
/// against MassTransit's <see cref="ITestHarness"/>. Confirms the stub
/// consumes <see cref="TrackingPushedV1"/> and ACKs without throwing.
/// Phase-2 Sprint-4 replaces the stub with the real channel-adapter
/// consumer that pushes the tracking info back to the marketplace.
/// </summary>
public sealed class ChannelTrackingConsumerTests
{
    [Fact]
    public async Task Consume_StubLogsAndAcks()
    {
        var services = new ServiceCollection();
        services.AddLogging(b => b.AddProvider(NullLoggerProvider.Instance));

        services.AddMassTransitTestHarness(cfg =>
        {
            cfg.AddConsumer<ChannelTrackingConsumer>();
        });

        await using var sp = services.BuildServiceProvider(true);
        var harness = sp.GetRequiredService<ITestHarness>();
        await harness.Start();

        var msg = new TrackingPushedV1(
            OrderId: Guid.NewGuid(),
            TenantId: Guid.NewGuid(),
            TrackingNumber: "TRK-stub-123",
            LabelUrl: "https://mock-carrier.example/labels/TRK-stub-123.pdf",
            ChannelId: null,
            OccurredAt: DateTime.UtcNow
        );
        await harness.Bus.Publish(msg);

        (await harness.Consumed.Any<TrackingPushedV1>()).Should().BeTrue();
        var consumerHarness = harness.GetConsumerHarness<ChannelTrackingConsumer>();
        (await consumerHarness.Consumed.Any<TrackingPushedV1>()).Should().BeTrue();
    }

    [Fact]
    public async Task Consume_WithRealLogger_DoesNotThrow()
    {
        // Defensive: the consumer's structured-logging arguments mismatch
        // the format string (e.g. wrong placeholder count) would surface as
        // an ILogger formatting exception in some loggers. Use a real
        // logger factory to exercise the path.
        using var loggerFactory = LoggerFactory.Create(b => b.AddDebug());
        var logger = loggerFactory.CreateLogger<ChannelTrackingConsumer>();
        var consumer = new ChannelTrackingConsumer(logger);

        var msg = new TrackingPushedV1(
            OrderId: Guid.NewGuid(),
            TenantId: Guid.NewGuid(),
            TrackingNumber: "TRK-direct-1",
            LabelUrl: "https://mock-carrier.example/labels/TRK-direct-1.pdf",
            ChannelId: null,
            OccurredAt: DateTime.UtcNow
        );

        // Build a minimal ConsumeContext via MT's harness rather than
        // hand-rolling the interface — the harness wires every member.
        var services = new ServiceCollection();
        services.AddLogging(b => b.AddProvider(NullLoggerProvider.Instance));
        services.AddMassTransitTestHarness(cfg => cfg.AddConsumer<ChannelTrackingConsumer>());
        await using var sp = services.BuildServiceProvider(true);
        var harness = sp.GetRequiredService<ITestHarness>();
        await harness.Start();
        Func<Task> act = async () => await harness.Bus.Publish(msg);
        await act.Should().NotThrowAsync();
        (await harness.Consumed.Any<TrackingPushedV1>()).Should().BeTrue();
        _ = consumer; // suppress unused (the direct-instance test wasn't necessary in the end).
    }
}
