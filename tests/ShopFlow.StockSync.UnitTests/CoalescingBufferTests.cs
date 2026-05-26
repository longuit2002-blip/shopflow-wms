using ShopFlow.StockSync.Application.Coalescing;

namespace ShopFlow.StockSync.UnitTests;

/// <summary>
/// Sprint-5 plan U3 — CoalescingBuffer behavioural contract. Locks the
/// last-by-ObservedAt overwrite, the snapshot-clear atomicity, and the
/// concurrent-write no-throw invariant. The flash-sale + per-channel
/// fan-out responsibilities live on the consumer (separate file) — this
/// file is buffer-only.
/// </summary>
public sealed class CoalescingBufferTests
{
    private static readonly Guid TenantA = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid TenantB = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly DateTime BaseTime = new(2026, 5, 16, 10, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Upsert_SameKey_OverwritesWhenObservedAtIsNewer()
    {
        var buffer = new CoalescingBuffer();
        var key = new CoalesceKey(TenantA, "SKU-X", "shopee");

        for (var i = 0; i < 10; i++)
        {
            buffer.Upsert(
                key,
                new CoalesceEntry(
                    AvailableToSell: 10 - i,
                    ObservedAt: BaseTime.AddMilliseconds(i),
                    IsFlashSale: false
                )
            );
        }

        var snapshot = buffer.SnapshotAndClear();

        snapshot.Should().HaveCount(1);
        snapshot[0].Key.Should().Be(key);
        snapshot[0]
            .Value.AvailableToSell.Should()
            .Be(1, "the 10th write had the latest ObservedAt and lowest stock");
        buffer.Count.Should().Be(0);
    }

    [Fact]
    public void Upsert_OutOfOrderObservedAt_KeepsLatestObservedTime()
    {
        var buffer = new CoalescingBuffer();
        var key = new CoalesceKey(TenantA, "SKU-X", "shopee");

        buffer.Upsert(key, new CoalesceEntry(5, BaseTime.AddSeconds(5), false));
        buffer.Upsert(key, new CoalesceEntry(99, BaseTime.AddSeconds(3), false));

        var snapshot = buffer.SnapshotAndClear();

        snapshot.Should().HaveCount(1);
        snapshot[0]
            .Value.AvailableToSell.Should()
            .Be(
                5,
                "last-by-write loses to last-by-ObservedAt — the older event arriving second must not regress published stock"
            );
        snapshot[0].Value.ObservedAt.Should().Be(BaseTime.AddSeconds(5));
    }

    [Fact]
    public void Upsert_MultipleKeys_TrackedIndependently()
    {
        var buffer = new CoalescingBuffer();
        var k1 = new CoalesceKey(TenantA, "SKU-X", "shopee");
        var k2 = new CoalesceKey(TenantA, "SKU-Y", "shopee");
        var k3 = new CoalesceKey(TenantB, "SKU-X", "shopee");

        // 3 writes on k1
        buffer.Upsert(k1, new CoalesceEntry(10, BaseTime, false));
        buffer.Upsert(k1, new CoalesceEntry(9, BaseTime.AddMilliseconds(1), false));
        buffer.Upsert(k1, new CoalesceEntry(8, BaseTime.AddMilliseconds(2), false));
        // 2 writes on k2
        buffer.Upsert(k2, new CoalesceEntry(7, BaseTime, false));
        buffer.Upsert(k2, new CoalesceEntry(6, BaseTime.AddMilliseconds(1), false));
        // 1 write on k3
        buffer.Upsert(k3, new CoalesceEntry(5, BaseTime, false));

        var snapshot = buffer.SnapshotAndClear();

        snapshot.Should().HaveCount(3);
        snapshot.Should().ContainSingle(kv => kv.Key == k1 && kv.Value.AvailableToSell == 8);
        snapshot.Should().ContainSingle(kv => kv.Key == k2 && kv.Value.AvailableToSell == 6);
        snapshot.Should().ContainSingle(kv => kv.Key == k3 && kv.Value.AvailableToSell == 5);
    }

    [Fact]
    public async Task Upsert_ConcurrentWrites_NeverThrowAndBoundedByUniqueKeys()
    {
        var buffer = new CoalescingBuffer();
        const int taskCount = 10;
        const int writesPerTask = 100;
        const int uniqueSkus = 5;

        var tasks = Enumerable
            .Range(0, taskCount)
            .Select(taskIdx =>
                Task.Run(() =>
                {
                    for (var i = 0; i < writesPerTask; i++)
                    {
                        var key = new CoalesceKey(TenantA, $"SKU-{i % uniqueSkus}", "shopee");
                        buffer.Upsert(
                            key,
                            new CoalesceEntry(
                                AvailableToSell: taskIdx * writesPerTask + i,
                                ObservedAt: BaseTime.AddMilliseconds(taskIdx * writesPerTask + i),
                                IsFlashSale: false
                            )
                        );
                    }
                })
            )
            .ToArray();

        await Task.WhenAll(tasks);

        buffer.Count.Should().BeLessThanOrEqualTo(uniqueSkus);
        buffer.Count.Should().BeGreaterThan(0);
        var snapshot = buffer.SnapshotAndClear();
        snapshot.Count.Should().BeLessThanOrEqualTo(uniqueSkus);
    }

    [Fact]
    public void SnapshotAndClear_OnEmptyBuffer_ReturnsEmpty()
    {
        var buffer = new CoalescingBuffer();

        var snapshot = buffer.SnapshotAndClear();

        snapshot.Should().BeEmpty();
    }

    [Fact]
    public void SnapshotAndClear_RemovesAllEntries()
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

        buffer.SnapshotAndClear();

        buffer.Count.Should().Be(0);
        buffer.SnapshotAndClear().Should().BeEmpty();
    }

    [Fact]
    public void Upsert_PreservesIsFlashSaleFromLatestEntry()
    {
        var buffer = new CoalescingBuffer();
        var key = new CoalesceKey(TenantA, "SKU-X", "shopee");

        buffer.Upsert(key, new CoalesceEntry(10, BaseTime, IsFlashSale: false));
        buffer.Upsert(key, new CoalesceEntry(5, BaseTime.AddMilliseconds(1), IsFlashSale: true));

        var snapshot = buffer.SnapshotAndClear();

        snapshot[0].Value.IsFlashSale.Should().BeTrue();
    }
}
