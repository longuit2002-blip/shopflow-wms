using System.Diagnostics;
using System.Threading.RateLimiting;
using Microsoft.Extensions.Options;
using ShopFlow.StockSync.Application.Options;
using ShopFlow.StockSync.Infrastructure.RateLimit;

namespace ShopFlow.StockSync.UnitTests.RateLimit;

/// <summary>
/// Sprint-5 plan U4 (R6) — <see cref="TenantChannelBucketRegistry"/>
/// behavioural contract. Locks down burst absorption, sustain
/// replenishment, per-<c>(tenant, channel)</c> independence, and
/// queue-limit rejection.
/// </summary>
/// <remarks>
/// Timing-sensitive tests assume the underlying
/// <c>TokenBucketRateLimiter</c> uses a 1s replenishment period; we
/// accept ±20% jitter to prevent flakiness on busy CI runners.
/// <c>Microsoft.Extensions.TimeProvider.Testing</c> is not on the CPM,
/// so we measure real wall time with relaxed assertions per the plan.
/// </remarks>
public sealed class TenantChannelBucketRegistryTests
{
    private static readonly Guid TenantA = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid TenantB = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private static TenantChannelBucketRegistry NewRegistry(
        int sustain = 10,
        int burst = 50,
        int queueLimit = 100
    )
    {
        var options = Options.Create(
            new StockSyncOptions
            {
                TokenBucket = new StockSyncOptions.TokenBucketSettings
                {
                    Sustain = sustain,
                    Burst = burst,
                    QueueLimit = queueLimit,
                },
            }
        );
        return new TenantChannelBucketRegistry(options);
    }

    [Fact]
    public async Task AcquireAsync_BurstSize_GrantsImmediately()
    {
        using var registry = NewRegistry(sustain: 10, burst: 50);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));

        var sw = Stopwatch.StartNew();
        var leases = new List<RateLimitLease>(50);
        for (var i = 0; i < 50; i++)
        {
            leases.Add(await registry.AcquireAsync(TenantA, "shopee", cts.Token));
        }
        sw.Stop();

        try
        {
            leases.Should().AllSatisfy(l => l.IsAcquired.Should().BeTrue());
            sw.ElapsedMilliseconds.Should()
                .BeLessThan(
                    500,
                    "burst of 50 tokens must be granted without waiting on replenishment"
                );
        }
        finally
        {
            foreach (var l in leases)
                l.Dispose();
        }
    }

    [Fact]
    public async Task AcquireAsync_BeyondBurst_ThrottlesToSustainRate()
    {
        // Burst = 20, Sustain = 10/s. 30 acquires = 20 instant + 10
        // over the next second → ≈ 1s wall time.
        using var registry = NewRegistry(sustain: 10, burst: 20, queueLimit: 100);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        var sw = Stopwatch.StartNew();
        var leases = new List<RateLimitLease>(30);
        for (var i = 0; i < 30; i++)
        {
            leases.Add(await registry.AcquireAsync(TenantA, "shopee", cts.Token));
        }
        sw.Stop();

        try
        {
            leases.Should().AllSatisfy(l => l.IsAcquired.Should().BeTrue());
            // 10 tokens to wait for at 10/s ≈ 1000ms; accept 600-2500ms
            // (generous to cover replenishment phase + scheduler jitter).
            sw.ElapsedMilliseconds.Should()
                .BeGreaterThan(
                    600,
                    "acquires past the burst must wait for the 1-second replenishment period"
                );
            sw.ElapsedMilliseconds.Should()
                .BeLessThan(
                    2_500,
                    "sustain rate (10/s) should drain the post-burst tokens within ~1s"
                );
        }
        finally
        {
            foreach (var l in leases)
                l.Dispose();
        }
    }

    [Fact]
    public async Task AcquireAsync_PerTenantChannelPair_IsIndependent()
    {
        using var registry = NewRegistry(sustain: 5, burst: 5);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));

        // Drain tenant A's (A, shopee) bucket to 0.
        var aLeases = new List<RateLimitLease>(5);
        for (var i = 0; i < 5; i++)
        {
            aLeases.Add(await registry.AcquireAsync(TenantA, "shopee", cts.Token));
        }

        // Tenant B's (B, shopee) acquire is on a separate bucket and
        // must complete instantly.
        var sw = Stopwatch.StartNew();
        using var bLease = await registry.AcquireAsync(TenantB, "shopee", cts.Token);
        sw.Stop();

        try
        {
            bLease
                .IsAcquired.Should()
                .BeTrue("tenant B's bucket is independent of tenant A's drain");
            sw.ElapsedMilliseconds.Should()
                .BeLessThan(
                    100,
                    "per-(tenant, channel) isolation means tenant B doesn't pay tenant A's wait"
                );
        }
        finally
        {
            foreach (var l in aLeases)
                l.Dispose();
        }
    }

    [Fact]
    public async Task AcquireAsync_QueueLimitOverflow_ReturnsUnacquiredLease()
    {
        // Burst = 1, QueueLimit = 1. After 1 instant + 1 queued, the
        // 3rd concurrent acquire trips QueueLimit and the lease arrives
        // unacquired.
        using var registry = NewRegistry(sustain: 1, burst: 1, queueLimit: 1);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));

        // First acquire — instant from burst.
        using var first = await registry.AcquireAsync(TenantA, "shopee", cts.Token);
        first.IsAcquired.Should().BeTrue();

        // Fire two more concurrently — one queues (succeeds eventually)
        // and one overflows (fails immediately).
        var t1 = registry.AcquireAsync(TenantA, "shopee", cts.Token).AsTask();
        var t2 = registry.AcquireAsync(TenantA, "shopee", cts.Token).AsTask();
        var t3 = registry.AcquireAsync(TenantA, "shopee", cts.Token).AsTask();

        var leases = await Task.WhenAll(t1, t2, t3);
        try
        {
            // At least one of the three must have been rejected because
            // QueueLimit = 1 caps the pending count.
            leases
                .Should()
                .Contain(
                    l => !l.IsAcquired,
                    "queue-limit overflow surfaces as an unacquired lease, not an exception"
                );

            var bucket = registry.GetOrCreate(TenantA, "shopee");
            bucket
                .GetStatistics()!
                .TotalFailedLeases.Should()
                .BeGreaterThan(
                    0,
                    "TokenBucketRateLimiter must record the QueueLimit rejection in its statistics counter"
                );
        }
        finally
        {
            foreach (var l in leases)
                l.Dispose();
        }
    }

    [Fact]
    public void GetOrCreate_SameTenantChannelPair_ReturnsSameInstance()
    {
        using var registry = NewRegistry();

        var first = registry.GetOrCreate(TenantA, "shopee");
        var second = registry.GetOrCreate(TenantA, "shopee");
        var other = registry.GetOrCreate(TenantA, "lazada");

        first.Should().BeSameAs(second, "buckets are keyed by (tenant, channel) pair");
        first
            .Should()
            .NotBeSameAs(other, "different channels for the same tenant get distinct buckets");
    }

    [Fact]
    public void Dispose_PreventsFurtherUse()
    {
        var registry = NewRegistry();
        registry.GetOrCreate(TenantA, "shopee");
        registry.Dispose();

        var act = () => registry.GetOrCreate(TenantA, "shopee");

        act.Should().Throw<ObjectDisposedException>();
    }
}
