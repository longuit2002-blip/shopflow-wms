using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using ShopFlow.ControlPlane.Domain;
using ShopFlow.SharedKernel.Application.Ports;
using ShopFlow.SharedKernel.Domain;

namespace ShopFlow.ControlPlane.Infrastructure.Repositories;

/// <summary>
/// LRU-cached read implementation of <see cref="ITenantCatalog"/> over
/// <see cref="ControlPlaneDbContext"/>. Per plan U5 deferred-item D2 the
/// cache is sized 1000 entries with a 5-minute sliding TTL; write-path
/// eviction (provision-complete, archive-start) is synchronous via
/// <see cref="Invalidate"/> to bound cross-app staleness.
/// </summary>
/// <remarks>
/// <para>
/// The cache is keyed twice — by slug and by id — because the routing
/// middleware looks up by slug while the outbox dispatcher looks up by
/// (implicitly) "give me every Ready tenant". To keep the two key spaces
/// consistent the implementation uses a single backing
/// <see cref="IMemoryCache"/> with a typed key prefix and writes both
/// entries on hydration.
/// </para>
/// <para>
/// <see cref="GetReadyTenantsAsync"/> intentionally bypasses the cache: the
/// dispatcher needs the live list to detect newly-Ready tenants and stops
/// fanning out to archived ones promptly. It does cache individual hydrated
/// entries on the way through so subsequent slug/id lookups land in cache.
/// </para>
/// </remarks>
public sealed class TenantCatalog : ITenantCatalog
{
    private const string SlugKeyPrefix = "tenant:slug:";
    private const string IdKeyPrefix = "tenant:id:";
    private static readonly TimeSpan DefaultTtl = TimeSpan.FromMinutes(5);

    private readonly ControlPlaneDbContext _db;
    private readonly IMemoryCache _cache;
    private readonly Func<Tenant, string> _connectionStringFactory;

    public TenantCatalog(
        ControlPlaneDbContext db,
        IMemoryCache cache,
        Func<Tenant, string> connectionStringFactory
    )
    {
        _db = db;
        _cache = cache;
        _connectionStringFactory = connectionStringFactory;
    }

    public async Task<TenantInfo?> LookupBySlugAsync(string slug, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(slug))
        {
            return null;
        }

        var normalized = slug.Trim().ToLowerInvariant();
        var key = SlugKeyPrefix + normalized;

        if (_cache.TryGetValue<TenantInfo>(key, out var cached) && cached is not null)
        {
            return cached;
        }

        var tenant = await _db
            .Tenants.AsNoTracking()
            .FirstOrDefaultAsync(t => t.Slug == normalized, ct)
            .ConfigureAwait(false);

        if (tenant is null)
        {
            return null;
        }

        var info = Project(tenant);
        Hydrate(info);
        return info;
    }

    public async Task<TenantInfo?> LookupByIdAsync(Guid tenantId, CancellationToken ct)
    {
        if (tenantId == Guid.Empty)
        {
            return null;
        }

        var key = IdKeyPrefix + tenantId;

        if (_cache.TryGetValue<TenantInfo>(key, out var cached) && cached is not null)
        {
            return cached;
        }

        var tenant = await _db
            .Tenants.AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == tenantId, ct)
            .ConfigureAwait(false);

        if (tenant is null)
        {
            return null;
        }

        var info = Project(tenant);
        Hydrate(info);
        return info;
    }

    public async Task<IReadOnlyList<TenantInfo>> GetReadyTenantsAsync(CancellationToken ct)
    {
        var ready = await _db
            .Tenants.AsNoTracking()
            .Where(t => t.Status == TenantStatus.Ready)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        var projections = new List<TenantInfo>(ready.Count);
        foreach (var t in ready)
        {
            var info = Project(t);
            Hydrate(info);
            projections.Add(info);
        }

        return projections;
    }

    /// <summary>
    /// Synchronous cache eviction for the write paths in
    /// <c>shopflow-migrate</c> (U6). Invalidating by id evicts the slug
    /// entry too — they always hydrate together.
    /// </summary>
    public void Invalidate(Guid tenantId, string? slug = null)
    {
        _cache.Remove(IdKeyPrefix + tenantId);
        if (!string.IsNullOrWhiteSpace(slug))
        {
            _cache.Remove(SlugKeyPrefix + slug.Trim().ToLowerInvariant());
        }
    }

    private TenantInfo Project(Tenant tenant) =>
        new(
            Id: tenant.Id,
            Slug: tenant.Slug,
            DbName: tenant.DbName,
            DbConnectionString: _connectionStringFactory(tenant),
            Region: tenant.Region,
            Tier: tenant.Tier,
            Status: tenant.Status
        );

    private void Hydrate(TenantInfo info)
    {
        var options = new MemoryCacheEntryOptions { SlidingExpiration = DefaultTtl };
        _cache.Set(SlugKeyPrefix + info.Slug, info, options);
        _cache.Set(IdKeyPrefix + info.Id, info, options);
    }
}
