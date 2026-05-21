using ShopFlow.Notification.Domain.Entities;

namespace ShopFlow.Notification.Application.Ports;

/// <summary>
/// Persistence boundary for the second-stage email queue
/// (<c>notification_outbox</c>). The U3 MT consumers <see cref="InsertAsync"/>
/// a row after rendering each incoming Sprint-9 cross-module event; the U3
/// background dispatcher claims pending rows in batches via
/// <see cref="ClaimPendingBatchAsync"/> (FOR UPDATE SKIP LOCKED) and
/// flips them through <see cref="UpdateAttemptAsync"/> (transient
/// failure → keep + bump <c>attempt_count</c>) or
/// <see cref="DeleteAsync"/> (terminal success or dead-letter).
/// </summary>
/// <remarks>
/// All methods are scoped to the per-request tenant's <c>NotificationDbContext</c>
/// (ADR-0003 — the DB identity IS the tenant boundary; no <c>tenant_id</c>
/// columns or per-call parameters).
/// </remarks>
public interface INotificationOutboxRepository
{
    /// <summary>
    /// Persist a freshly-rendered email. Returns the row id (= generated
    /// or supplied <see cref="NotificationOutboxEntry.Id"/>). Idempotency
    /// is not enforced here — the U3 dispatcher's <c>notification_log</c>
    /// UNIQUE (KTD3) catches duplicates downstream.
    /// </summary>
    Task<Guid> InsertAsync(NotificationOutboxEntry entry, CancellationToken ct);

    /// <summary>
    /// Atomically claim up to <paramref name="batchSize"/> oldest pending
    /// rows via <c>FOR UPDATE SKIP LOCKED</c>; safe for multiple
    /// dispatcher instances under Aspire scale-out. Caller is expected to
    /// process the returned rows inside the same transaction or close to
    /// it (Postgres releases the lock at COMMIT/ROLLBACK).
    /// </summary>
    Task<IReadOnlyList<NotificationOutboxEntry>> ClaimPendingBatchAsync(
        int batchSize,
        CancellationToken ct
    );

    /// <summary>
    /// After a transient failure, bump <c>attempt_count</c> and stamp
    /// <c>last_attempt_at</c> + <c>last_error_code</c> + <c>updated_at</c>
    /// so the next poll cycle picks the row back up.
    /// </summary>
    Task UpdateAttemptAsync(
        Guid id,
        int newAttemptCount,
        DateTime attemptedAt,
        string lastErrorCode,
        CancellationToken ct
    );

    /// <summary>
    /// Remove the outbox row — called by the dispatcher on terminal
    /// success (in the same transaction as the <c>notification_log</c>
    /// INSERT) or terminal failure (alongside a <c>notification_dead_letter</c>
    /// row insertion).
    /// </summary>
    Task DeleteAsync(Guid id, CancellationToken ct);
}
