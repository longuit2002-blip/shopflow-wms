using ShopFlow.StockSync.Domain.Aggregates;

namespace ShopFlow.StockSync.Application.Ports;

/// <summary>
/// Persistence port for the <c>stock_sync_push_log</c> audit table per
/// Sprint-5 plan U5 / R12. The dispatcher writes one row per push
/// attempt (Success / Failed / BreakerOpen) using the factories on
/// <see cref="PushLogEntry"/>; this port hides EF Core +
/// UNIQUE-23505-on-<c>idempotency_key</c> idempotency from the caller.
/// </summary>
/// <remarks>
/// <para>The repository swallows
/// <c>PostgresErrorCodes.UniqueViolation</c> on insert: the entry's
/// idempotency key is the deterministic
/// <c>tenantId:sku:channel:observedAt</c> string built by
/// <c>PushIntent.BuildIdempotencyKey</c>, so a second insert with the
/// same key represents MassTransit at-least-once redelivery — silently
/// ignoring it preserves the "exactly one row per observed reading"
/// invariant.</para>
///
/// <para>Sprint-1-redux <c>ReservationRepository</c> established the
/// UNIQUE-23505 catch pattern; this port reuses it verbatim.</para>
/// </remarks>
public interface IPushLogRepository
{
    /// <summary>
    /// Persist <paramref name="entry"/>. Returns when the row is in
    /// the database. If a row with the same <c>IdempotencyKey</c>
    /// already exists, this method returns successfully without
    /// re-inserting (UNIQUE-23505 caught + ignored).
    /// </summary>
    Task AppendAsync(PushLogEntry entry, CancellationToken ct);
}
