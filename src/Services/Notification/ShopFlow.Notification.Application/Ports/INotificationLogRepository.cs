using ShopFlow.Notification.Domain.Entities;

namespace ShopFlow.Notification.Application.Ports;

/// <summary>
/// Persistence boundary for the terminal-success log
/// (<c>notification_log</c>) and the terminal-failure store
/// (<c>notification_dead_letter</c>). The U3 dispatcher writes here
/// after each <c>IMailerProvider.SendAsync</c> resolves; concrete
/// implementations rely on the UNIQUE on <c>(source_event_id,
/// recipient_email)</c> (KTD3) to silently drop a duplicate redelivery
/// at debug log level.
/// </summary>
/// <remarks>
/// Methods scoped to the per-request tenant's <c>NotificationDbContext</c>
/// per ADR-0003.
/// </remarks>
public interface INotificationLogRepository
{
    /// <summary>
    /// Insert a success row. Returns <c>true</c> on a clean insert,
    /// <c>false</c> when the KTD3 UNIQUE constraint blocks a duplicate
    /// (Npgsql SQLState <c>23505</c>) — caller treats the false case as
    /// "already-sent" and drops the outbox row silently.
    /// </summary>
    Task<bool> TryInsertSuccessAsync(NotificationLogEntry entry, CancellationToken ct);

    /// <summary>
    /// Insert a dead-letter row capturing the final attempt's error
    /// shape. Always best-effort — duplicate dead-letter inserts are
    /// rare (the outbox row is deleted alongside) and acceptable; no
    /// UNIQUE guards this table.
    /// </summary>
    Task InsertDeadLetterAsync(NotificationDeadLetterEntry entry, CancellationToken ct);
}
