using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ShopFlow.SharedKernel.Application;
using ShopFlow.SharedKernel.Application.Ports;
using ShopFlow.StockSync.Application.Ports;

namespace ShopFlow.StockSync.Infrastructure.Persistence.Repositories;

/// <summary>
/// Singleton-lifetime decorator that wraps the scoped DB-backed
/// <see cref="SkuFlagRepository"/> with a 5-minute in-memory cache
/// keyed by <c>(tenantId, sku)</c> per Sprint-5 plan U7 (R10 hot-path).
/// </summary>
/// <remarks>
/// <para>The flush dispatcher (<c>CoalesceFlushService</c>) and the
/// consumer (<c>StockLevelChangedConsumer</c>) call
/// <see cref="IsFlashSaleAsync"/> potentially thousands of times per
/// second under a flash-sale burst. A naïve scoped repository would
/// open a DbContext + run a primary-key lookup per call. The cache
/// collapses that to one DB hit per <c>(tenantId, sku)</c> per 5
/// minutes; the admin <c>PUT /api/skus/{sku}/flag</c> path evicts the
/// entry on write so a freshly-flipped flag is visible immediately.</para>
///
/// <para>The DI scope dance: this decorator is registered Singleton, but
/// the inner <see cref="SkuFlagRepository"/> is Scoped (it depends on
/// a per-tenant <see cref="ShopFlow.StockSync.Infrastructure.StockSyncDbContext"/>).
/// On a cache miss / write the decorator opens a new
/// <see cref="IServiceScope"/>, resolves
/// <see cref="ShopFlow.SharedKernel.Application.RequestContext"/> from
/// it, calls <see cref="RequestContext.Bind"/> with the tenant
/// resolved through <see cref="ITenantCatalog"/>, and only then
/// resolves the scoped inner repo — the DbContext factory inside the
/// scope reads the bound connection string and builds the right
/// per-tenant context. Mirrors the K12 pattern used by
/// <c>PerTenantDispatcherService.AppendLogAsync</c>.</para>
///
/// <para>LRU eviction is intentionally crude: when the dictionary hits
/// <see cref="MaxEntries"/>, one arbitrary key is dropped to make
/// room. A real LRU (touch-on-read, ordered eviction) is a Phase-3
/// upgrade — for the Sprint-5 scale gate the working set is bounded
/// by distinct flagged SKUs per tenant (single-digit thousands).</para>
/// </remarks>
public sealed class CachingSkuFlagRepository : ISkuFlagRepository
{
    /// <summary>Cache freshness window (R10 / U7 plan default).</summary>
    public static readonly TimeSpan Ttl = TimeSpan.FromMinutes(5);

    /// <summary>Soft cap on cache size — crude LRU eviction kicks in here.</summary>
    public const int MaxEntries = 10_000;

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ITenantCatalog _tenantCatalog;
    private readonly TimeProvider _clock;
    private readonly ILogger<CachingSkuFlagRepository> _logger;
    private readonly Func<IServiceProvider, ISkuFlagRepository> _innerResolver;
    private readonly ConcurrentDictionary<CacheKey, CacheSlot> _cache = new();

    public CachingSkuFlagRepository(
        IServiceScopeFactory scopeFactory,
        ITenantCatalog tenantCatalog,
        TimeProvider clock,
        ILogger<CachingSkuFlagRepository> logger
    )
        : this(
            scopeFactory,
            tenantCatalog,
            clock,
            logger,
            innerResolver: static sp => sp.GetRequiredService<SkuFlagRepository>()
        )
    { }

    /// <summary>
    /// Test seam — lets unit tests supply a stub inner repo instead of the
    /// DB-backed <see cref="SkuFlagRepository"/>. The DI scope is still
    /// created so the <see cref="RequestContext"/> binding contract is
    /// exercised. Production callers use the default constructor above.
    /// </summary>
    public CachingSkuFlagRepository(
        IServiceScopeFactory scopeFactory,
        ITenantCatalog tenantCatalog,
        TimeProvider clock,
        ILogger<CachingSkuFlagRepository> logger,
        Func<IServiceProvider, ISkuFlagRepository> innerResolver
    )
    {
        ArgumentNullException.ThrowIfNull(scopeFactory);
        ArgumentNullException.ThrowIfNull(tenantCatalog);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(innerResolver);

        _scopeFactory = scopeFactory;
        _tenantCatalog = tenantCatalog;
        _clock = clock;
        _logger = logger;
        _innerResolver = innerResolver;
    }

    public async Task<bool> IsFlashSaleAsync(Guid tenantId, string sku, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(sku))
        {
            return false;
        }

        var key = new CacheKey(tenantId, sku);
        var now = _clock.GetUtcNow();

        if (_cache.TryGetValue(key, out var slot) && slot.ExpiresAt > now)
        {
            return slot.IsFlashSale;
        }

        var fresh = await WithTenantScopeAsync(
            tenantId,
            ct,
            (inner, scopeCt) => inner.IsFlashSaleAsync(tenantId, sku, scopeCt)
        ).ConfigureAwait(false);

        StoreInCache(key, fresh, now);
        return fresh;
    }

    public async Task SetFlashSaleAsync(
        Guid tenantId,
        string sku,
        bool isFlashSale,
        CancellationToken ct
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sku);

        await WithTenantScopeAsync(
            tenantId,
            ct,
            async (inner, scopeCt) =>
            {
                await inner.SetFlashSaleAsync(tenantId, sku, isFlashSale, scopeCt)
                    .ConfigureAwait(false);
                return true;
            }
        ).ConfigureAwait(false);

        // Eviction-on-write: the next read for this (tenant, sku) takes
        // the DB path and re-populates the cache with the new value.
        _cache.TryRemove(new CacheKey(tenantId, sku), out _);
    }

    public async Task<bool> ApplyEventAsync(
        Guid tenantId,
        string sku,
        bool isFlashSale,
        DateTime occurredAt,
        CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sku);

        var applied = await WithTenantScopeAsync(
            tenantId,
            ct,
            (inner, scopeCt) => inner.ApplyEventAsync(tenantId, sku, isFlashSale, occurredAt, scopeCt)
        ).ConfigureAwait(false);

        // Only evict the cache when the inner write actually landed —
        // stale events (older OccurredAt than stored) leave the cache
        // untouched because the stored value is still correct.
        if (applied)
        {
            _cache.TryRemove(new CacheKey(tenantId, sku), out _);
        }
        return applied;
    }

    private async Task<T> WithTenantScopeAsync<T>(
        Guid tenantId,
        CancellationToken ct,
        Func<ISkuFlagRepository, CancellationToken, Task<T>> work
    )
    {
        var tenant = await _tenantCatalog
            .LookupByIdAsync(tenantId, ct)
            .ConfigureAwait(false)
            ?? throw new InvalidOperationException(
                $"CachingSkuFlagRepository: tenant {tenantId} not found in catalog. "
                + "The flush + consumer paths must only ask for tenants the catalog knows about."
            );

        await using var scope = _scopeFactory.CreateAsyncScope();
        var requestContext = scope.ServiceProvider.GetRequiredService<RequestContext>();
        requestContext.Bind(tenant, Guid.NewGuid().ToString("N"), userId: null);

        var inner = _innerResolver(scope.ServiceProvider);
        try
        {
            return await work(inner, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "CachingSkuFlagRepository: inner repo call failed for tenant {TenantSlug}.",
                tenant.Slug
            );
            throw;
        }
    }

    private void StoreInCache(CacheKey key, bool isFlashSale, DateTimeOffset now)
    {
        if (_cache.Count >= MaxEntries)
        {
            // Crude LRU — drop one arbitrary entry to make room. Phase-3
            // swap for a real touch-on-read LRU once the working set
            // approaches the cap in production telemetry. Take a snapshot
            // of the first key from the enumerator; the dictionary may
            // mutate concurrently, so TryRemove is a best-effort.
            foreach (var existingKey in _cache.Keys)
            {
                _cache.TryRemove(existingKey, out _);
                break;
            }
        }

        _cache[key] = new CacheSlot(isFlashSale, now + Ttl);
    }

    /// <summary>Exposed for diagnostics + tests; not part of the public port.</summary>
    public int CacheSize => _cache.Count;

    private readonly record struct CacheKey(Guid TenantId, string Sku);

    private readonly record struct CacheSlot(bool IsFlashSale, DateTimeOffset ExpiresAt);
}
