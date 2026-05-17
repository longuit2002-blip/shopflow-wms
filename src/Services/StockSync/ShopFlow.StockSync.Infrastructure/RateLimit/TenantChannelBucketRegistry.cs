using System.Collections.Concurrent;
using System.Threading.RateLimiting;
using Microsoft.Extensions.Options;
using ShopFlow.StockSync.Application.Options;

namespace ShopFlow.StockSync.Infrastructure.RateLimit;

/// <summary>
/// Sprint-5 plan U4 (R6) — per-<c>(tenant, channel)</c>
/// <see cref="TokenBucketRateLimiter"/> registry. The dispatcher (U5)
/// calls <see cref="AcquireAsync"/> before invoking the marketplace
/// adapter so noisy-neighbor traffic from one tenant can't saturate the
/// outbound channel and starve other tenants — Sprint-5 headline scale
/// gate (R6 + KTD3).
/// </summary>
/// <remarks>
/// <para>Each <c>(tenant, channel)</c> pair gets its own bucket created
/// lazily via <c>ConcurrentDictionary.GetOrAdd</c>. Defaults come from
/// <see cref="StockSyncOptions.TokenBucket"/> — Sprint-5 ships one
/// template for every pair; per-tenant overrides arrive Phase-3.</para>
///
/// <para><see cref="AcquireAsync"/> returns the lease so the caller can
/// observe acquisition success (<c>lease.IsAcquired</c>) and dispose at
/// the end of the protected operation. Callers must <c>using</c> the
/// lease — that's the standard <c>RateLimiter</c> contract.</para>
///
/// <para>Registered as <c>Singleton</c> + <c>IDisposable</c> in
/// <c>AddStockSyncModule</c> (U8) so the DI container disposes every
/// bucket when the host shuts down.</para>
/// </remarks>
public sealed class TenantChannelBucketRegistry : IDisposable
{
    private readonly ConcurrentDictionary<(Guid TenantId, string ChannelType), TokenBucketRateLimiter> _buckets = new();
    private readonly StockSyncOptions.TokenBucketSettings _settings;
    private int _disposed;

    public TenantChannelBucketRegistry(IOptions<StockSyncOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _settings = options.Value.TokenBucket;
    }

    /// <summary>
    /// Acquires one token for the <c>(tenantId, channelType)</c> bucket,
    /// awaiting bucket replenishment when the burst budget is exhausted.
    /// The returned lease must be disposed by the caller; inspect
    /// <c>lease.IsAcquired</c> before treating the slot as granted —
    /// <see cref="StockSyncOptions.TokenBucketSettings.QueueLimit"/>
    /// overflow surfaces as <c>IsAcquired = false</c>.
    /// </summary>
    public ValueTask<RateLimitLease> AcquireAsync(
        Guid tenantId, string channelType, CancellationToken ct
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(channelType);
        ThrowIfDisposed();

        var bucket = GetOrCreate(tenantId, channelType);
        return bucket.AcquireAsync(permitCount: 1, cancellationToken: ct);
    }

    /// <summary>
    /// Returns the underlying limiter for <c>(tenantId, channelType)</c>,
    /// creating it if needed. Exposed primarily for the U8 diagnostics
    /// endpoint + <see cref="TokenBucketRateLimiter.GetStatistics"/>
    /// inspection in tests.
    /// </summary>
    public TokenBucketRateLimiter GetOrCreate(Guid tenantId, string channelType)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(channelType);
        ThrowIfDisposed();

        return _buckets.GetOrAdd(
            (tenantId, channelType),
            static (_, state) => CreateBucket(state),
            _settings
        );
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        foreach (var bucket in _buckets.Values)
        {
            bucket.Dispose();
        }
        _buckets.Clear();
    }

    private void ThrowIfDisposed()
    {
        if (Volatile.Read(ref _disposed) != 0)
        {
            throw new ObjectDisposedException(nameof(TenantChannelBucketRegistry));
        }
    }

    private static TokenBucketRateLimiter CreateBucket(StockSyncOptions.TokenBucketSettings settings)
    {
        return new TokenBucketRateLimiter(new TokenBucketRateLimiterOptions
        {
            TokenLimit = settings.Burst,
            TokensPerPeriod = settings.Sustain,
            ReplenishmentPeriod = TimeSpan.FromSeconds(1),
            AutoReplenishment = true,
            QueueLimit = settings.QueueLimit,
            QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
        });
    }
}
