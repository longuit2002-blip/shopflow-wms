using Microsoft.EntityFrameworkCore;
using Npgsql;
using ShopFlow.StockSync.Application.Ports;
using ShopFlow.StockSync.Domain.Aggregates;

namespace ShopFlow.StockSync.Infrastructure.Persistence.Repositories;

/// <summary>
/// EF Core implementation of <see cref="IPushLogRepository"/> against
/// the scoped per-tenant <see cref="StockSyncDbContext"/> (Sprint-5
/// plan U5 / R12). The 23505 catch makes the repository idempotent on
/// the <c>ux_stock_sync_push_log_idempotency</c> UNIQUE — same
/// <c>idempotency_key</c> ⇒ no second row.
/// </summary>
/// <remarks>
/// <para>The dispatcher writes one row per push attempt using
/// <see cref="PushLogEntry.MarkSucceeded"/> /
/// <see cref="PushLogEntry.MarkFailed"/> /
/// <see cref="PushLogEntry.MarkBreakerOpen"/>. Under MassTransit
/// at-least-once redelivery the same intent may be processed twice;
/// the UNIQUE constraint collapses the duplicate into a no-op write,
/// matching the Sprint-1-redux <c>ReservationRepository</c> pattern.</para>
///
/// <para>The detach-on-failure dance is required because EF Core's
/// change tracker still has the failed-insert entity in
/// <see cref="EntityState.Added"/>; subsequent calls on the same
/// <see cref="DbContext"/> would re-try the insert on next
/// <c>SaveChangesAsync</c>. Detaching breaks that loop without
/// dropping any pending tracked state for other entities.</para>
/// </remarks>
public sealed class PushLogRepository : IPushLogRepository
{
    private readonly StockSyncDbContext _db;

    public PushLogRepository(StockSyncDbContext db)
    {
        ArgumentNullException.ThrowIfNull(db);
        _db = db;
    }

    public async Task AppendAsync(PushLogEntry entry, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(entry);

        await _db.PushLogEntries.AddAsync(entry, ct).ConfigureAwait(false);

        try
        {
            await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        }
        catch (DbUpdateException ex)
            when (ex.InnerException is PostgresException pg
                && pg.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            // MassTransit redelivery (or retry-after-cooldown re-emit)
            // produced the same idempotency key. The first row is
            // canonical; ignore this insert. Detach to keep the
            // DbContext's change tracker clean.
            _db.Entry(entry).State = EntityState.Detached;
        }
    }
}
