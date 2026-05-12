using ShopFlow.Inventory.Domain;
using ShopFlow.SharedKernel.Domain;

namespace ShopFlow.Inventory.Application.Ports;

/// <summary>
/// Write surface for the append-only reservation ledger. The flagship
/// method is <see cref="TryReserveAsync"/> which implements the
/// conditional-INSERT pattern (CTE INSERT … WHERE NOT EXISTS, READ
/// COMMITTED) per Tech Design v3.0 §4.4 — the load-bearing correctness
/// guarantee against the flash-sale hot-key race.
/// </summary>
/// <remarks>
/// <para>Idempotency anchor is the <c>UNIQUE(order_id)</c> constraint;
/// duplicate-order retries surface as a no-row-inserted outcome the
/// implementation must distinguish from "insufficient stock". Per
/// AGENTS.md §6.39 webhook receivers and command handlers persist the
/// idempotency key before invoking this port — duplicates resolve to
/// the existing reservation rather than a second TryReserve.</para>
///
/// <para>U8 ships the port; Sprint-1-redux (plan 003) ships the
/// implementation. The repository skeleton in <c>Infrastructure</c>
/// throws <see cref="NotImplementedException"/>; integration and
/// property tests against the ledger spec stay red until then.</para>
/// </remarks>
public interface IReservationRepository
{
    /// <summary>
    /// Attempt to reserve <paramref name="quantity"/> of <paramref name="sku"/>
    /// for <paramref name="orderId"/>. Returns the created
    /// <see cref="Reservation"/> on success; <see cref="Result.Failure"/>
    /// with code <c>reservation.insufficient_stock</c> on oversold;
    /// returns the existing row on idempotent re-attempt.
    /// </summary>
    Task<Result<Reservation>> TryReserveAsync(
        Sku sku,
        string orderId,
        Quantity quantity,
        TimeSpan ttl,
        CancellationToken ct
    );

    Task<Reservation?> FindByOrderIdAsync(string orderId, CancellationToken ct);

    /// <summary>
    /// Transition Pending → Confirmed. Caller (handler) catches
    /// idempotent re-attempts via <see cref="Reservation.Status"/>
    /// inspection before delegating here.
    /// </summary>
    Task<Result> ConfirmAsync(string orderId, CancellationToken ct);

    /// <summary>
    /// Transition Pending → Released (explicit cancellation).
    /// </summary>
    Task<Result> ReleaseAsync(string orderId, CancellationToken ct);

    /// <summary>
    /// Background-worker entry point — finds Pending rows with
    /// <c>ExpiresAt &lt; now</c>, transitions them to Expired in batches.
    /// Returns the count of rows expired in this run.
    /// </summary>
    Task<int> ReleaseExpiredAsync(DateTime now, int batchSize, CancellationToken ct);
}
