using Microsoft.EntityFrameworkCore;
using ShopFlow.Notification.Application.Ports;
using ShopFlow.Notification.Domain.Entities;

namespace ShopFlow.Notification.Infrastructure.Repositories;

/// <summary>
/// EF Core impl of <see cref="INotificationOutboxRepository"/>. Scoped
/// per request — binds to the per-tenant
/// <see cref="NotificationDbContext"/> from the DI scope.
/// </summary>
public sealed class NotificationOutboxRepository : INotificationOutboxRepository
{
    private readonly NotificationDbContext _db;

    public NotificationOutboxRepository(NotificationDbContext db)
    {
        _db = db;
    }

    public async Task<Guid> InsertAsync(NotificationOutboxEntry entry, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(entry);

        if (entry.Id == Guid.Empty)
        {
            entry.Id = Guid.NewGuid();
        }
        entry.CreatedAt = DateTime.UtcNow;
        entry.UpdatedAt = entry.CreatedAt;

        await _db.NotificationOutbox.AddAsync(entry, ct).ConfigureAwait(false);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);

        return entry.Id;
    }

    public async Task<IReadOnlyList<NotificationOutboxEntry>> ClaimPendingBatchAsync(
        int batchSize,
        CancellationToken ct
    )
    {
        // FOR UPDATE SKIP LOCKED — multiple dispatcher instances (e.g.
        // under Aspire scale-out) can poll concurrently without
        // double-claiming. The "sending" status flip + UpdateAttemptAsync
        // are the dispatcher's responsibility once claimed; this method
        // just returns the row set.
        //
        // The raw-SQL path is necessary because EF's LINQ provider
        // doesn't translate to SKIP LOCKED on its own.
        var batch = await _db
            .NotificationOutbox.FromSqlRaw(
                "SELECT * FROM notification_outbox "
                    + "WHERE status = 'pending' "
                    + "ORDER BY created_at "
                    + "LIMIT {0} "
                    + "FOR UPDATE SKIP LOCKED",
                batchSize
            )
            .AsNoTracking()
            .ToListAsync(ct)
            .ConfigureAwait(false);

        return batch;
    }

    public async Task UpdateAttemptAsync(
        Guid id,
        int newAttemptCount,
        DateTime attemptedAt,
        string lastErrorCode,
        CancellationToken ct
    )
    {
        var entity = await _db
            .NotificationOutbox.FirstOrDefaultAsync(o => o.Id == id, ct)
            .ConfigureAwait(false);
        if (entity is null)
        {
            return;
        }

        entity.AttemptCount = newAttemptCount;
        entity.LastAttemptAt = attemptedAt;
        entity.LastErrorCode = lastErrorCode;
        entity.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct)
    {
        var rows = await _db
            .NotificationOutbox.Where(o => o.Id == id)
            .ExecuteDeleteAsync(ct)
            .ConfigureAwait(false);

        // No-op when row is already gone — race-safe with concurrent
        // dispatcher instances claiming the same row before SKIP LOCKED
        // would normally guard. We don't error on missing row.
        _ = rows;
    }
}
