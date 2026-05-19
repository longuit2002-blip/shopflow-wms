using Microsoft.EntityFrameworkCore;
using Npgsql;
using ShopFlow.StockSync.Application.Ports;
using ShopFlow.StockSync.Domain.Aggregates;

namespace ShopFlow.StockSync.Infrastructure.Persistence.Repositories;

/// <summary>
/// EF Core implementation of <see cref="ISkuFlagRepository"/> against the
/// scoped per-tenant <see cref="StockSyncDbContext"/> (Sprint-5 plan U7).
/// </summary>
/// <remarks>
/// <para>This is the DB-backed inner repo. The caching layer
/// (<c>CachingSkuFlagRepository</c>) wraps it and opens a tenant-bound
/// DI scope per call; this class trusts that the injected DbContext is
/// already pointed at the right tenant's database.</para>
///
/// <para>The <paramref name="tenantId"/> parameter on each method is
/// informational here — ADR-0003 says no tenant column lives on the
/// table — but it stays on the port so the caching decorator can key
/// its cache by <c>(tenantId, sku)</c>.</para>
///
/// <para><see cref="SetFlashSaleAsync"/> mirrors Sprint-4's
/// <c>ProductMappingRepository</c>: try the INSERT, catch the
/// 23505 UNIQUE violation on the SKU primary key, detach the rejected
/// entity, load the existing row, and apply the domain's idempotent
/// <see cref="SkuFlag.SetFlashSale"/>. The "do not touch the row when
/// the value is unchanged" semantics live on the aggregate, not here.</para>
/// </remarks>
public sealed class SkuFlagRepository : ISkuFlagRepository
{
    private readonly StockSyncDbContext _db;

    public SkuFlagRepository(StockSyncDbContext db)
    {
        ArgumentNullException.ThrowIfNull(db);
        _db = db;
    }

    public async Task<bool> IsFlashSaleAsync(Guid tenantId, string sku, CancellationToken ct)
    {
        _ = tenantId;
        if (string.IsNullOrWhiteSpace(sku))
        {
            return false;
        }

        var flag = await _db
            .SkuFlags.AsNoTracking()
            .FirstOrDefaultAsync(f => f.Sku == sku, ct)
            .ConfigureAwait(false);

        return flag is not null && flag.IsFlashSale;
    }

    public async Task SetFlashSaleAsync(
        Guid tenantId,
        string sku,
        bool isFlashSale,
        CancellationToken ct
    )
    {
        _ = tenantId;
        ArgumentException.ThrowIfNullOrWhiteSpace(sku);

        var newFlag = SkuFlag.Create(sku, isFlashSale);
        await _db.SkuFlags.AddAsync(newFlag, ct).ConfigureAwait(false);

        try
        {
            await _db.SaveChangesAsync(ct).ConfigureAwait(false);
            return;
        }
        catch (DbUpdateException ex)
            when (ex.InnerException is PostgresException pg
                && pg.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            // Concurrent admin write (or test priming) already inserted a
            // row with the same SKU primary key. Detach to keep the
            // change-tracker clean, then load + apply the idempotent
            // domain setter. The aggregate's SetFlashSale is a no-op
            // when the requested value equals the current value, so
            // duplicate writes don't bump updated_at unnecessarily.
            _db.Entry(newFlag).State = EntityState.Detached;
        }

        var existing = await _db
            .SkuFlags.FirstAsync(f => f.Sku == sku, ct)
            .ConfigureAwait(false);
        existing.SetFlashSale(isFlashSale);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    public async Task<bool> ApplyEventAsync(
        Guid tenantId,
        string sku,
        bool isFlashSale,
        DateTime occurredAt,
        CancellationToken ct)
    {
        _ = tenantId;
        ArgumentException.ThrowIfNullOrWhiteSpace(sku);

        // Look up existing row to apply the OccurredAt guard before any
        // write. If the stored row's effective timestamp
        // (UpdatedAt ?? CreatedAt) is newer than the incoming event,
        // drop the write — Sprint-7.5 KTD3.
        var existing = await _db
            .SkuFlags.AsTracking()
            .FirstOrDefaultAsync(f => f.Sku == sku, ct)
            .ConfigureAwait(false);

        if (existing is not null)
        {
            var storedAt = existing.UpdatedAt ?? existing.CreatedAt;
            if (storedAt > occurredAt)
            {
                // Stale write — log + skip. The caller surfaces this as
                // a "stale flash-sale event dropped" Debug entry.
                return false;
            }

            existing.SetFlashSale(isFlashSale);
            await _db.SaveChangesAsync(ct).ConfigureAwait(false);
            return true;
        }

        // No prior row: insert. Race with another consumer is caught by
        // UNIQUE-23505 fallback below.
        var fresh = SkuFlag.Create(sku, isFlashSale);
        await _db.SkuFlags.AddAsync(fresh, ct).ConfigureAwait(false);

        try
        {
            await _db.SaveChangesAsync(ct).ConfigureAwait(false);
            return true;
        }
        catch (DbUpdateException ex)
            when (ex.InnerException is PostgresException pg
                && pg.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            _db.Entry(fresh).State = EntityState.Detached;
        }

        // Re-resolve + re-apply the guard now that the racing row landed.
        var landed = await _db
            .SkuFlags.AsTracking()
            .FirstAsync(f => f.Sku == sku, ct)
            .ConfigureAwait(false);
        var landedAt = landed.UpdatedAt ?? landed.CreatedAt;
        if (landedAt > occurredAt)
        {
            return false;
        }
        landed.SetFlashSale(isFlashSale);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return true;
    }
}
