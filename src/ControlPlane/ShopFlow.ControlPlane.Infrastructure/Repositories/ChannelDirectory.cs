using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using ShopFlow.ControlPlane.Application.Ports;

namespace ShopFlow.ControlPlane.Infrastructure.Repositories;

/// <summary>
/// LRU-cached read implementation of <see cref="IChannelDirectory"/> over
/// <see cref="ControlPlaneDbContext"/>. Mirrors <c>TenantCatalog</c>'s
/// cache discipline (5-minute sliding TTL, synchronous eviction on write).
/// </summary>
public sealed class ChannelDirectory : IChannelDirectory
{
    private const string KeyPrefix = "channel:";
    private static readonly TimeSpan DefaultTtl = TimeSpan.FromMinutes(5);

    private readonly ControlPlaneDbContext _db;
    private readonly IMemoryCache _cache;

    public ChannelDirectory(ControlPlaneDbContext db, IMemoryCache cache)
    {
        _db = db;
        _cache = cache;
    }

    public async Task<ChannelTenantBinding?> LookupAsync(Guid channelId, CancellationToken ct)
    {
        if (channelId == Guid.Empty)
        {
            return null;
        }

        var key = KeyPrefix + channelId;

        if (_cache.TryGetValue<ChannelTenantBinding>(key, out var cached) && cached is not null)
        {
            return cached;
        }

        var row = await _db
            .ChannelConnections.AsNoTracking()
            .FirstOrDefaultAsync(c => c.ChannelId == channelId, ct)
            .ConfigureAwait(false);

        if (row is null)
        {
            return null;
        }

        var binding = new ChannelTenantBinding(
            ChannelId: row.ChannelId,
            TenantId: row.TenantId,
            ChannelType: row.ChannelType,
            SecretEncrypted: row.SecretEncrypted
        );

        // Size = 1 is mandatory: AddMemoryCache sets SizeLimit = 1000 (D2),
        // and a Set without a size throws when SizeLimit is set. Same
        // never-run gap as TenantCatalog.Hydrate (finish-line U7).
        _cache.Set(
            key,
            binding,
            new MemoryCacheEntryOptions { SlidingExpiration = DefaultTtl, Size = 1 }
        );
        return binding;
    }

    public void Invalidate(Guid channelId) => _cache.Remove(KeyPrefix + channelId);
}
