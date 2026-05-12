using ShopFlow.SharedKernel.Domain;

namespace ShopFlow.Inventory.Domain;

/// <summary>
/// One row of the append-only reservation ledger per Tech Design v3.0 §4.2
/// — the hot-key flash-sale solution. The ledger is "append-only" in the
/// sense that the conditional-INSERT pattern (CTE INSERT … WHERE NOT EXISTS,
/// READ COMMITTED) decides at write time whether a new <see cref="Reservation"/>
/// row materialises; Confirm/Release/Expire transitions update the row's
/// <see cref="Status"/>, which is what makes the ledger queryable.
/// </summary>
/// <remarks>
/// <para>Idempotency anchor: the <c>(order_id) UNIQUE</c> constraint per
/// Tech Design v3.0 §4.2 — duplicate reservation attempts for the same
/// <c>order_id</c> are caught at the index level rather than in
/// application code. Per ADR-0003 this is <c>UNIQUE(order_id)</c>, NOT
/// <c>UNIQUE(tenant_id, order_id)</c> — the tenant DB is the boundary.</para>
///
/// <para>U8 ships the schema and the Pending/Confirmed/Released/Expired
/// state machine surface; <c>TryReserve</c>, <c>Confirm</c>, <c>Release</c>,
/// <c>ReleaseExpired</c> behavior lands in Sprint-1-redux (plan 003).</para>
/// </remarks>
public sealed class Reservation : BaseEntity
{
    public Sku Sku { get; private set; } = default!;

    public string OrderId { get; private set; } = string.Empty;

    public Quantity Quantity { get; private set; } = Quantity.Zero;

    public ReservationStatus Status { get; private set; } = ReservationStatus.Pending;

    /// <summary>
    /// Wall-clock deadline after which a Pending reservation is eligible
    /// for the expiry worker. Tech Design v3.0 §4.2 defaults to 15 minutes
    /// post-creation; per-channel overrides land in Sprint-1-redux.
    /// </summary>
    public DateTime ExpiresAt { get; private set; }

    public DateTime? ConfirmedAt { get; private set; }

    public DateTime? ReleasedAt { get; private set; }

    public DateTime? ExpiredAt { get; private set; }

    private Reservation() { }

    /// <summary>
    /// Build a Pending reservation. Validation only — the conditional INSERT
    /// against the ledger (which decides whether sufficient stock exists)
    /// lives in the repository (<c>IReservationRepository.TryReserveAsync</c>)
    /// and is Sprint-1-redux.
    /// </summary>
    public static Result<Reservation> Create(
        Sku sku,
        string orderId,
        Quantity quantity,
        TimeSpan ttl,
        DateTime now
    )
    {
        ArgumentNullException.ThrowIfNull(sku);
        ArgumentNullException.ThrowIfNull(quantity);

        if (string.IsNullOrWhiteSpace(orderId))
        {
            return Result<Reservation>.Failure(
                "order_id is required",
                "reservation.order_id_required"
            );
        }
        if (quantity.Value == 0)
        {
            return Result<Reservation>.Failure(
                "quantity must be > 0",
                "reservation.quantity_zero"
            );
        }
        if (ttl <= TimeSpan.Zero)
        {
            return Result<Reservation>.Failure(
                "ttl must be > 0",
                "reservation.ttl_non_positive"
            );
        }

        return Result<Reservation>.Success(
            new Reservation
            {
                Sku = sku,
                OrderId = orderId.Trim(),
                Quantity = quantity,
                Status = ReservationStatus.Pending,
                ExpiresAt = now + ttl,
            }
        );
    }

    /// <summary>
    /// Confirm a Pending reservation (stock leaves the warehouse).
    /// Sprint-1-redux behavior.
    /// </summary>
    public Result Confirm(DateTime now)
    {
        _ = now;
        throw new NotImplementedException(
            "Sprint-1-redux behavior — see docs/plans/2026-05-11-003-phase-1-sprint-1-redux-reservation-ledger-plan.md"
        );
    }

    /// <summary>
    /// Release a Pending reservation (cancellation). Sprint-1-redux behavior.
    /// </summary>
    public Result Release(DateTime now)
    {
        _ = now;
        throw new NotImplementedException(
            "Sprint-1-redux behavior — see docs/plans/2026-05-11-003-phase-1-sprint-1-redux-reservation-ledger-plan.md"
        );
    }

    /// <summary>
    /// Expire a Pending reservation past its TTL. Called by
    /// <c>ReservationExpiryWorker</c>. Sprint-1-redux behavior.
    /// </summary>
    public Result Expire(DateTime now)
    {
        _ = now;
        throw new NotImplementedException(
            "Sprint-1-redux behavior — see docs/plans/2026-05-11-003-phase-1-sprint-1-redux-reservation-ledger-plan.md"
        );
    }
}
