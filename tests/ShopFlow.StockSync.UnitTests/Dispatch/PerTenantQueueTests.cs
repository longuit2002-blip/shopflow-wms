using Microsoft.Extensions.Options;
using ShopFlow.StockSync.Application.Dispatch;
using ShopFlow.StockSync.Application.Options;
using ShopFlow.StockSync.Infrastructure.Dispatch;

namespace ShopFlow.StockSync.UnitTests.Dispatch;

/// <summary>
/// Sprint-5 plan U4 — <see cref="PerTenantQueue"/> behavioural
/// contract. Locks down strict high-priority preference, DropOldest
/// semantics on overflow, and per-tenant isolation. These invariants
/// underpin AE5 (flash-sale lane never starved) and R10 (queue
/// fairness during noisy-neighbor scale gate).
/// </summary>
public sealed class PerTenantQueueTests
{
    private static readonly Guid TenantA = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid TenantB = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly DateTime BaseTime = new(2026, 5, 16, 10, 0, 0, DateTimeKind.Utc);

    private static PerTenantQueue NewQueue(int highCap = 1_000, int normalCap = 10_000)
    {
        var options = Options.Create(
            new StockSyncOptions
            {
                QueueCapacity = new StockSyncOptions.QueueCapacitySettings
                {
                    HighCap = highCap,
                    NormalCap = normalCap,
                },
            }
        );
        return new PerTenantQueue(options);
    }

    private static PushIntent Intent(
        Guid tenantId,
        string sku,
        bool flash,
        int available = 0,
        int observedMs = 0
    )
    {
        var observedAt = BaseTime.AddMilliseconds(observedMs);
        return new PushIntent(
            TenantId: tenantId,
            Sku: sku,
            ChannelType: "shopee",
            Available: available,
            ObservedAt: observedAt,
            IsFlashSale: flash,
            IdempotencyKey: PushIntent.BuildIdempotencyKey(tenantId, sku, "shopee", observedAt)
        );
    }

    [Fact]
    public async Task ReadNextAsync_HighPriorityDrainsFirst_EvenWithNormalQueued()
    {
        var queue = NewQueue();
        var ct = TestCt(TimeSpan.FromSeconds(2));

        // Pre-load 100 normal entries.
        for (var i = 0; i < 100; i++)
        {
            await queue.EnqueueAsync(Intent(TenantA, $"SKU-{i}", flash: false, available: i), ct);
        }
        // Then one flash-sale entry.
        await queue.EnqueueAsync(Intent(TenantA, "SKU-FLASH", flash: true, available: 999), ct);

        // The very first read must surface the flash-sale entry.
        var first = await queue.ReadNextAsync(TenantA, ct);

        first
            .Sku.Should()
            .Be("SKU-FLASH", "high lane has strict priority over a fully-loaded normal lane");
        first.IsFlashSale.Should().BeTrue();
    }

    [Fact]
    public async Task ReadNextAsync_BothLanesEmpty_BlocksUntilCancelled()
    {
        var queue = NewQueue();
        // GetOrCreate happens inside ReadNextAsync — no warm-up needed.
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));

        var act = async () => await queue.ReadNextAsync(TenantA, cts.Token);

        await act.Should()
            .ThrowAsync<OperationCanceledException>(
                "with both lanes empty the reader must block until either an item arrives or the token cancels"
            );
    }

    [Fact]
    public async Task ReadNextAsync_HighPreferred_WhenInterleavedWithNormal()
    {
        var queue = NewQueue();
        var ct = TestCt(TimeSpan.FromSeconds(2));

        await queue.EnqueueAsync(Intent(TenantA, "N1", flash: false, available: 1), ct);
        await queue.EnqueueAsync(Intent(TenantA, "H1", flash: true, available: 99), ct);
        await queue.EnqueueAsync(Intent(TenantA, "N2", flash: false, available: 2), ct);

        var first = await queue.ReadNextAsync(TenantA, ct);
        var second = await queue.ReadNextAsync(TenantA, ct);
        var third = await queue.ReadNextAsync(TenantA, ct);

        // High lane drains first; normal lane preserves FIFO order.
        first.Sku.Should().Be("H1");
        second.Sku.Should().Be("N1");
        third.Sku.Should().Be("N2");
    }

    [Fact]
    public async Task ReadNextAsync_HighLaneDropsOldest_WhenCapacityExceeded()
    {
        // HighCap = 3; write 5 flash-sale entries — the first two must
        // be dropped (DropOldest semantics) and reads return 3, 4, 5.
        var queue = NewQueue(highCap: 3, normalCap: 10);
        var ct = TestCt(TimeSpan.FromSeconds(2));

        for (var i = 1; i <= 5; i++)
        {
            await queue.EnqueueAsync(
                Intent(TenantA, $"H{i}", flash: true, available: i, observedMs: i),
                ct
            );
        }

        var r1 = await queue.ReadNextAsync(TenantA, ct);
        var r2 = await queue.ReadNextAsync(TenantA, ct);
        var r3 = await queue.ReadNextAsync(TenantA, ct);

        r1.Available.Should()
            .Be(3, "oldest two entries dropped — high-lane capacity 3 keeps the latest three");
        r2.Available.Should().Be(4);
        r3.Available.Should().Be(5);
    }

    [Fact]
    public async Task EnqueueAsync_TenantsAreIsolated_NoCrossTenantBleed()
    {
        var queue = NewQueue(highCap: 2, normalCap: 2);
        var ct = TestCt(TimeSpan.FromSeconds(2));

        // Saturate tenant A normal lane (4 writes against cap=2 → drops 1,2; keeps 3,4).
        for (var i = 1; i <= 4; i++)
        {
            await queue.EnqueueAsync(
                Intent(TenantA, $"A-N{i}", flash: false, available: i, observedMs: i),
                ct
            );
        }
        // Tenant B writes one normal entry — must be unaffected.
        await queue.EnqueueAsync(
            Intent(TenantB, "B-N1", flash: false, available: 100, observedMs: 100),
            ct
        );

        var aFirst = await queue.ReadNextAsync(TenantA, ct);
        var aSecond = await queue.ReadNextAsync(TenantA, ct);
        var bFirst = await queue.ReadNextAsync(TenantB, ct);

        aFirst.Available.Should().Be(3, "tenant A dropped its oldest entries on overflow");
        aSecond.Available.Should().Be(4);
        bFirst.Sku.Should().Be("B-N1", "tenant B's lane is independent — no cross-tenant drop");
        bFirst.Available.Should().Be(100);
    }

    /// <summary>
    /// Bounded-wall-time cancellation token for tests so a stuck wait
    /// fails fast rather than hanging the whole suite.
    /// </summary>
    private static CancellationToken TestCt(TimeSpan budget)
    {
        var cts = new CancellationTokenSource(budget);
        return cts.Token;
    }
}
