using Microsoft.EntityFrameworkCore;
using Npgsql;
using ShopFlow.Channel.Application.Ports;
using ShopFlow.Channel.Domain.ProductMappings;
using ShopFlow.SharedKernel.Domain;

namespace ShopFlow.Channel.Infrastructure.Repositories;

/// <summary>
/// EF Core + Npgsql implementation of <see cref="IProductMappingRepository"/>
/// per Sprint-4 plan U6. <see cref="UpsertManualAsync"/> handles admin
/// POST idempotency via the UNIQUE-23505 catch + lookup pattern.
/// </summary>
public sealed class ProductMappingRepository : IProductMappingRepository
{
    private readonly ChannelDbContext _db;

    public ProductMappingRepository(ChannelDbContext db)
    {
        _db = db;
    }

    public async Task<Result<ProductMapping>> UpsertManualAsync(
        Guid channelId,
        ExternalSku externalSku,
        string internalSku,
        CancellationToken ct
    )
    {
        ArgumentNullException.ThrowIfNull(externalSku);

        var newMapping = ProductMapping.Create(
            channelId,
            externalSku,
            internalSku,
            MappingMethod.Manual,
            confidence: 1m
        );
        if (!newMapping.IsSuccess)
        {
            return newMapping;
        }

        await _db.ProductMappings.AddAsync(newMapping.Value!, ct).ConfigureAwait(false);
        try
        {
            await _db.SaveChangesAsync(ct).ConfigureAwait(false);
            return newMapping;
        }
        catch (DbUpdateException ex)
            when (ex.InnerException is PostgresException pg
                && pg.SqlState == PostgresErrorCodes.UniqueViolation
            )
        {
            _db.Entry(newMapping.Value!).State = EntityState.Detached;

            var existing = await FindExactAsync(channelId, externalSku, ct).ConfigureAwait(false);
            if (existing is null)
            {
                return Result<ProductMapping>.Failure(
                    "idempotency conflict but no existing mapping found.",
                    "mapping.idempotency_conflict_no_row"
                );
            }
            return Result<ProductMapping>.Success(existing);
        }
    }

    public Task<ProductMapping?> FindExactAsync(
        Guid channelId,
        ExternalSku externalSku,
        CancellationToken ct
    )
    {
        ArgumentNullException.ThrowIfNull(externalSku);
        return _db
            .ProductMappings.AsNoTracking()
            .FirstOrDefaultAsync(m => m.ChannelId == channelId && m.ExternalSku == externalSku, ct);
    }

    public async Task<IReadOnlyList<ProductMapping>> ListByChannelAsync(
        Guid channelId,
        int page,
        int pageSize,
        CancellationToken ct
    )
    {
        if (page < 1)
            page = 1;
        if (pageSize < 1)
            pageSize = 50;
        if (pageSize > 500)
            pageSize = 500;

        return await _db
            .ProductMappings.AsNoTracking()
            .Where(m => m.ChannelId == channelId)
            .OrderBy(m => m.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct)
            .ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<ProductMapping>> ReadAllByChannelAsync(
        Guid channelId,
        CancellationToken ct
    )
    {
        return await _db
            .ProductMappings.AsNoTracking()
            .Where(m => m.ChannelId == channelId)
            .ToListAsync(ct)
            .ConfigureAwait(false);
    }
}
