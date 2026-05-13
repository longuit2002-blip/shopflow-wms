using ShopFlow.Inventory.Domain;
using ShopFlow.SharedKernel.Domain;

namespace ShopFlow.Inventory.Application.Ports;

/// <summary>
/// Write surface for the append-only reservation ledger. The flagship
/// method is <see cref="TryReserveAsync"/> which implements the
/// conditional-INSERT pattern (CTE INSERT … WHERE NOT EXISTS, READ
/// COMMITTED) per Tech Design v3.0 §4.4 — the load-bearing correctness
/// guarantee against the flash-sale hot-key race. Sprint-3-redux K11 adds
/// the multi-line variant <see cref="TryReserveLinesAsync"/> as an
/// all-or-nothing CTE so multi-line orders insert N rows atomically.
/// </summary>
/// <remarks>
/// <para>Idempotency anchor was <c>UNIQUE(order_id)</c> in Sprint-1-redux;
/// Sprint-3-redux K10 moves it to composite <c>UNIQUE(order_id, order_line_id)</c>.
/// Duplicate-order retries still surface as a no-row-inserted outcome the
/// implementation distinguishes from "insufficient stock". Per AGENTS.md §6.39
/// webhook receivers and command handlers persist the idempotency key
/// before invoking this port — duplicates resolve to the existing
/// reservation rather than a second TryReserve.</para>
///
/// <para>U8 ships the port; Sprint-1-redux (plan 003) ships the
/// single-line implementation. Sprint-3-redux U3 adds the multi-line
/// methods + delegates the existing single-line wrapper through them.</para>
/// </remarks>
public interface IReservationRepository
{
    /// <summary>
    /// Attempt to reserve <paramref name="quantity"/> of <paramref name="sku"/>
    /// for <paramref name="orderId"/>. Returns the created
    /// <see cref="Reservation"/> on success; <see cref="Result{T}.Failure"/>
    /// with code <c>reservation.insufficient_stock</c> on oversold;
    /// returns the existing row on idempotent re-attempt.
    /// </summary>
    /// <remarks>
    /// Sprint-3-redux U3 routes this through <see cref="TryReserveLinesAsync"/>
    /// internally with <c>order_line_id='_default'</c>; external behavior
    /// is unchanged.
    /// </remarks>
    Task<Result<Reservation>> TryReserveAsync(
        Sku sku,
        string orderId,
        Quantity quantity,
        TimeSpan ttl,
        CancellationToken ct
    );

    /// <summary>
    /// Multi-line all-or-nothing reservation per Sprint-3-redux K11.
    /// Inserts N rows into <c>reservations_ledger</c> sharing
    /// <paramref name="orderId"/> with distinct <c>order_line_id</c>s,
    /// decrements <c>stock_items.available</c> for each, all in one
    /// CTE — if any line oversells the whole call is a no-op. Returns
    /// per-line outcomes on both success and atomic-failure so the
    /// saga's compensation path knows which lines individually had
    /// stock available.
    /// </summary>
    /// <remarks>
    /// Idempotency: redelivery of the same <c>(orderId, lines)</c> hits
    /// 23505 on the composite UNIQUE; the repository catches +
    /// re-reads the existing rows + returns them as
    /// <see cref="TryReserveLinesResult.Success"/>.
    /// </remarks>
    Task<TryReserveLinesResult> TryReserveLinesAsync(
        string orderId,
        IReadOnlyList<LineReservation> lines,
        TimeSpan ttl,
        CancellationToken ct
    );

    Task<Reservation?> FindByOrderIdAsync(string orderId, CancellationToken ct);

    /// <summary>
    /// Transition Pending → Confirmed for ALL ledger rows under
    /// <paramref name="orderId"/>. Sprint-3-redux: the underlying SQL
    /// already matches every row with the given <c>order_id</c>, so
    /// multi-line orders confirm as a unit.
    /// </summary>
    Task<Result> ConfirmAsync(string orderId, CancellationToken ct);

    /// <summary>
    /// Transition Pending → Released for ALL ledger rows under
    /// <paramref name="orderId"/>. Sprint-3-redux: full-order release;
    /// for partial-set compensation use <see cref="ReleaseLinesAsync"/>.
    /// </summary>
    Task<Result> ReleaseAsync(string orderId, CancellationToken ct);

    /// <summary>
    /// Partial-set release per Sprint-3-redux K11 — release ONLY the
    /// rows whose <c>order_line_id</c> appears in
    /// <paramref name="orderLineIds"/>. Returns the actually-released
    /// line ids in <see cref="ReleaseLinesResult.ReleasedLineIds"/> so
    /// the consumer can emit a <c>StockReleasedV1</c> with the precise
    /// list (saga uses this for Set-based dedup against MassTransit
    /// at-least-once redelivery).
    /// </summary>
    /// <remarks>
    /// Idempotency: the WHERE clause is <c>status = 'Pending'</c>; rows
    /// already released are skipped silently. Re-delivery returns an
    /// empty <see cref="ReleaseLinesResult.ReleasedLineIds"/>.
    /// </remarks>
    Task<ReleaseLinesResult> ReleaseLinesAsync(
        string orderId,
        IReadOnlyList<string> orderLineIds,
        CancellationToken ct
    );

    /// <summary>
    /// Background-worker entry point — finds Pending rows with
    /// <c>ExpiresAt &lt; now</c>, transitions them to Expired in batches.
    /// Returns the count of rows expired in this run.
    /// </summary>
    Task<int> ReleaseExpiredAsync(DateTime now, int batchSize, CancellationToken ct);
}
