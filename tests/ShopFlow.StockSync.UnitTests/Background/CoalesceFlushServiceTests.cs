using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using ShopFlow.StockSync.Application.Coalescing;
using ShopFlow.StockSync.Application.Dispatch;
using ShopFlow.StockSync.Application.Options;
using ShopFlow.StockSync.Infrastructure.Background;

namespace ShopFlow.StockSync.UnitTests.Background;

/// <summary>
/// Sprint-5 plan U3 — <c>CoalesceFlushService</c> drain-and-enqueue
/// contract. Tests invoke <c>FlushAsync</c> directly to avoid paying
/// the <c>PeriodicTimer</c> wall-clock latency; one end-to-end test
/// spins <c>ExecuteAsync</c> with a 50ms window to lock the timer
/// wiring itself.
/// </summary>
public sealed class CoalesceFlushServiceTests
{
    private static readonly Guid TenantA = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly DateTime BaseTime = new(2026, 5, 16, 10, 0, 0, DateTimeKind.Utc);

    private static StockSyncOptions NewOptions(int windowMs = 500) =>
        new() { CoalesceWindowMs = windowMs };

    [Fact]
    public async Task FlushAsync_EmptyBuffer_DoesNotEnqueue()
    {
        var buffer = new CoalescingBuffer();
        var queue = Substitute.For<IPerTenantQueue>();
        var svc = NewService(buffer, queue);

        await svc.FlushAsync(CancellationToken.None);

        await queue
            .DidNotReceive()
            .EnqueueAsync(Arg.Any<PushIntent>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task FlushAsync_DrainsBufferAndEnqueuesOneIntentPerEntry()
    {
        var buffer = new CoalescingBuffer();
        buffer.Upsert(
            new CoalesceKey(TenantA, "SKU-X", "shopee"),
            new CoalesceEntry(7, BaseTime, IsFlashSale: false)
        );
        buffer.Upsert(
            new CoalesceKey(TenantA, "SKU-Y", "shopee"),
            new CoalesceEntry(3, BaseTime, IsFlashSale: true)
        );
        buffer.Upsert(
            new CoalesceKey(TenantA, "SKU-X", "lazada"),
            new CoalesceEntry(7, BaseTime, IsFlashSale: false)
        );

        var queue = Substitute.For<IPerTenantQueue>();
        var svc = NewService(buffer, queue);

        await svc.FlushAsync(CancellationToken.None);

        await queue.Received(3).EnqueueAsync(Arg.Any<PushIntent>(), Arg.Any<CancellationToken>());
        buffer.Count.Should().Be(0);
    }

    [Fact]
    public async Task FlushAsync_BuildsCanonicalIdempotencyKey()
    {
        var buffer = new CoalescingBuffer();
        var key = new CoalesceKey(TenantA, "SKU-X", "shopee");
        buffer.Upsert(key, new CoalesceEntry(7, BaseTime, IsFlashSale: false));

        var captured = new List<PushIntent>();
        var queue = Substitute.For<IPerTenantQueue>();
        queue
            .EnqueueAsync(Arg.Any<PushIntent>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                captured.Add(call.Arg<PushIntent>());
                return ValueTask.CompletedTask;
            });

        var svc = NewService(buffer, queue);
        await svc.FlushAsync(CancellationToken.None);

        captured.Should().HaveCount(1);
        var intent = captured[0];
        intent
            .IdempotencyKey.Should()
            .Be(PushIntent.BuildIdempotencyKey(TenantA, "SKU-X", "shopee", BaseTime));
        intent.TenantId.Should().Be(TenantA);
        intent.Sku.Should().Be("SKU-X");
        intent.ChannelType.Should().Be("shopee");
        intent.Available.Should().Be(7);
        intent.ObservedAt.Should().Be(BaseTime);
        intent.IsFlashSale.Should().BeFalse();
    }

    [Fact]
    public async Task FlushAsync_PreservesIsFlashSaleFlagOnIntent()
    {
        var buffer = new CoalescingBuffer();
        buffer.Upsert(
            new CoalesceKey(TenantA, "SKU-FLASH", "shopee"),
            new CoalesceEntry(2, BaseTime, IsFlashSale: true)
        );

        var captured = new List<PushIntent>();
        var queue = Substitute.For<IPerTenantQueue>();
        queue
            .EnqueueAsync(Arg.Any<PushIntent>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                captured.Add(call.Arg<PushIntent>());
                return ValueTask.CompletedTask;
            });

        var svc = NewService(buffer, queue);
        await svc.FlushAsync(CancellationToken.None);

        captured.Should().HaveCount(1);
        captured[0].IsFlashSale.Should().BeTrue();
    }

    [Fact]
    public async Task FlushAsync_EnqueueFault_DoesNotStopOtherEntries()
    {
        var buffer = new CoalescingBuffer();
        buffer.Upsert(
            new CoalesceKey(TenantA, "SKU-X", "shopee"),
            new CoalesceEntry(1, BaseTime, false)
        );
        buffer.Upsert(
            new CoalesceKey(TenantA, "SKU-Y", "shopee"),
            new CoalesceEntry(2, BaseTime, false)
        );
        buffer.Upsert(
            new CoalesceKey(TenantA, "SKU-Z", "shopee"),
            new CoalesceEntry(3, BaseTime, false)
        );

        var queue = Substitute.For<IPerTenantQueue>();
        var callCount = 0;
        queue
            .When(q => q.EnqueueAsync(Arg.Any<PushIntent>(), Arg.Any<CancellationToken>()))
            .Do(_ =>
            {
                callCount++;
                if (callCount == 2)
                {
                    throw new InvalidOperationException("simulated queue saturation");
                }
            });

        var svc = NewService(buffer, queue);

        // Must NOT throw — the loop swallows per-entry faults so one
        // bad enqueue can't strand the other coalesced entries.
        await svc.FlushAsync(CancellationToken.None);

        callCount.Should().Be(3, "every entry must be attempted even when one throws");
    }

    [Fact]
    public async Task ExecuteAsync_FlushesOnceWindowElapses()
    {
        // End-to-end test against the real PeriodicTimer using a tiny
        // 50ms window so the test isn't slow. Verifies the timer wiring
        // — not the per-entry drain (that's covered above).
        var buffer = new CoalescingBuffer();
        buffer.Upsert(
            new CoalesceKey(TenantA, "SKU-X", "shopee"),
            new CoalesceEntry(7, BaseTime, false)
        );

        var queue = Substitute.For<IPerTenantQueue>();

        var svc = NewService(buffer, queue, windowMs: 50);
        using var cts = new CancellationTokenSource();

        await svc.StartAsync(cts.Token);

        // Let three windows pass to ensure at least one tick fired.
        await Task.Delay(200, CancellationToken.None);
        await svc.StopAsync(CancellationToken.None);

        await queue
            .Received()
            .EnqueueAsync(
                Arg.Is<PushIntent>(p => p.Sku == "SKU-X" && p.Available == 7),
                Arg.Any<CancellationToken>()
            );
        buffer.Count.Should().Be(0);
    }

    [Fact]
    public async Task ExecuteAsync_NeverFlushes_BeforeFirstWindowElapses()
    {
        var buffer = new CoalescingBuffer();
        buffer.Upsert(
            new CoalesceKey(TenantA, "SKU-X", "shopee"),
            new CoalesceEntry(7, BaseTime, false)
        );

        var queue = Substitute.For<IPerTenantQueue>();

        // 500ms window. Within the first ~100ms we should observe zero
        // flushes — proves the timer doesn't fire spuriously on tick 0.
        var svc = NewService(buffer, queue, windowMs: 500);
        using var cts = new CancellationTokenSource();
        await svc.StartAsync(cts.Token);

        await Task.Delay(100, CancellationToken.None);

        await queue
            .DidNotReceive()
            .EnqueueAsync(Arg.Any<PushIntent>(), Arg.Any<CancellationToken>());

        await svc.StopAsync(CancellationToken.None);
    }

    private static CoalesceFlushService NewService(
        ICoalescingBuffer buffer,
        IPerTenantQueue queue,
        int windowMs = 500
    ) =>
        new(
            buffer,
            queue,
            Microsoft.Extensions.Options.Options.Create(NewOptions(windowMs)),
            TimeProvider.System,
            NullLogger<CoalesceFlushService>.Instance
        );
}
