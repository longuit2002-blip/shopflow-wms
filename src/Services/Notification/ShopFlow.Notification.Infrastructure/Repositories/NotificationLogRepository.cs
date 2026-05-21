using Microsoft.EntityFrameworkCore;
using Npgsql;
using ShopFlow.Notification.Application.Ports;
using ShopFlow.Notification.Domain.Entities;

namespace ShopFlow.Notification.Infrastructure.Repositories;

/// <summary>
/// EF Core impl of <see cref="INotificationLogRepository"/>. Catches
/// Npgsql SQLState <c>23505</c> on the KTD3 UNIQUE
/// <c>(source_event_id, recipient_email)</c> and collapses to a
/// <c>false</c> return so the U3 dispatcher can silently drop the
/// duplicate outbox row at debug log level — no double-send.
/// </summary>
public sealed class NotificationLogRepository : INotificationLogRepository
{
    private const string PostgresUniqueViolation = "23505";

    private readonly NotificationDbContext _db;

    public NotificationLogRepository(NotificationDbContext db)
    {
        _db = db;
    }

    public async Task<bool> TryInsertSuccessAsync(
        NotificationLogEntry entry,
        CancellationToken ct
    )
    {
        ArgumentNullException.ThrowIfNull(entry);

        if (entry.Id == Guid.Empty)
        {
            entry.Id = Guid.NewGuid();
        }
        entry.CreatedAt = DateTime.UtcNow;
        entry.UpdatedAt = entry.CreatedAt;

        try
        {
            await _db.NotificationLog.AddAsync(entry, ct).ConfigureAwait(false);
            await _db.SaveChangesAsync(ct).ConfigureAwait(false);
            return true;
        }
        catch (DbUpdateException ex)
            when (ex.InnerException is PostgresException pex
                && string.Equals(
                    pex.SqlState,
                    PostgresUniqueViolation,
                    StringComparison.Ordinal
                ))
        {
            // Detach the failed row from the change tracker so future
            // SaveChanges on the same DbContext don't replay the
            // attempted insert.
            _db.Entry(entry).State = EntityState.Detached;
            return false;
        }
    }

    public async Task InsertDeadLetterAsync(
        NotificationDeadLetterEntry entry,
        CancellationToken ct
    )
    {
        ArgumentNullException.ThrowIfNull(entry);

        if (entry.Id == Guid.Empty)
        {
            entry.Id = Guid.NewGuid();
        }
        entry.CreatedAt = DateTime.UtcNow;
        entry.UpdatedAt = entry.CreatedAt;

        await _db.NotificationDeadLetter.AddAsync(entry, ct).ConfigureAwait(false);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
    }
}
