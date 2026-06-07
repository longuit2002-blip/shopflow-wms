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
    /// <summary>
    /// Default line id stamped on single-line reservations from the
    /// Sprint-1-redux <see cref="IReservationRepository"/> wrapper path so
    /// the composite UNIQUE <c>(order_id, order_line_id)</c> (per K10/K11)
    /// still anchors idempotency for legacy callers. Multi-line orders
    /// (Sprint-3-redux) pass Outbound's <c>order_lines.id</c> string instead.
    /// </summary>
    public const string DefaultOrderLineId = "_default";

    public Sku Sku { get; private set; } = default!;

    public string OrderId { get; private set; } = string.Empty;

    /// <summary>
    /// Per-line id under <see cref="OrderId"/>, per K10/K11. Defaults to
    /// <see cref="DefaultOrderLineId"/> for single-line Sprint-1-redux
    /// callers; multi-line Sprint-3-redux callers pass the Outbound
    /// <c>order_lines.id</c> stringified.
    /// </summary>
    public string OrderLineId { get; private set; } = DefaultOrderLineId;

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
    /// lives in the repository (<c>IReservationRepository.TryReserveAsync</c>
    /// or its multi-line variant <c>TryReserveLinesAsync</c>) and is
    /// Sprint-1-redux / Sprint-3-redux U3.
    /// </summary>
    /// <remarks>
    /// <paramref name="orderLineId"/> defaults to
    /// <see cref="DefaultOrderLineId"/> for the single-line wrapper path so
    /// pre-Sprint-3 callers keep working without changes; Sprint-3-redux
    /// multi-line orders pass each line's Outbound <c>order_lines.id</c>.
    /// </remarks>
    public static Result<Reservation> Create(
        Sku sku,
        string orderId,
        Quantity quantity,
        TimeSpan ttl,
        DateTime now,
        string? orderLineId = null
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
            return Result<Reservation>.Failure("quantity must be > 0", "reservation.quantity_zero");
        }
        if (ttl <= TimeSpan.Zero)
        {
            return Result<Reservation>.Failure("ttl must be > 0", "reservation.ttl_non_positive");
        }

        var resolvedLineId = string.IsNullOrWhiteSpace(orderLineId)
            ? DefaultOrderLineId
            : orderLineId.Trim();

        return Result<Reservation>.Success(
            new Reservation
            {
                Sku = sku,
                OrderId = orderId.Trim(),
                OrderLineId = resolvedLineId,
                Quantity = quantity,
                Status = ReservationStatus.Pending,
                ExpiresAt = now + ttl,
            }
        );
    }

    /// <summary>
    /// Confirm a Pending reservation (stock physically leaves the warehouse).
    /// Pending → Confirmed; terminal state. Caller (repository) is responsible
    /// for emitting the corresponding outbox row in the same transaction.
    /// </summary>
    public Result Confirm(DateTime now)
    {
        if (Status == ReservationStatus.Confirmed)
        {
            return Result.Failure("already confirmed.", "reservation.already_confirmed");
        }
        if (Status != ReservationStatus.Pending)
        {
            return Result.Failure(
                $"cannot confirm reservation in {Status} state.",
                "reservation.invalid_state"
            );
        }

        Status = ReservationStatus.Confirmed;
        ConfirmedAt = now;
        UpdatedAt = now;
        return Result.Success();
    }

    /// <summary>
    /// Release a Pending reservation (explicit cancellation by the order owner).
    /// Pending → Released; terminal state. Held units return to the available
    /// pool.
    /// </summary>
    public Result Release(DateTime now)
    {
        if (Status == ReservationStatus.Released)
        {
            return Result.Failure("already released.", "reservation.already_released");
        }
        if (Status != ReservationStatus.Pending)
        {
            return Result.Failure(
                $"cannot release reservation in {Status} state.",
                "reservation.invalid_state"
            );
        }

        Status = ReservationStatus.Released;
        ReleasedAt = now;
        UpdatedAt = now;
        return Result.Success();
    }

    /// <summary>
    /// Expire a Pending reservation past its TTL — called by
    /// <see cref="Workers.ReservationExpiryWorker"/>. Pending → Expired;
    /// terminal state distinguished from Released so dashboards can surface
    /// TTL-driven losses separately from explicit cancellations.
    /// </summary>
    public Result Expire(DateTime now)
    {
        if (Status == ReservationStatus.Expired)
        {
            return Result.Failure("already expired.", "reservation.already_expired");
        }
        if (Status != ReservationStatus.Pending)
        {
            return Result.Failure(
                $"cannot expire reservation in {Status} state.",
                "reservation.invalid_state"
            );
        }
        if (now < ExpiresAt)
        {
            return Result.Failure(
                "reservation has not yet reached its expiry.",
                "reservation.not_yet_expired"
            );
        }

        Status = ReservationStatus.Expired;
        ExpiredAt = now;
        UpdatedAt = now;
        return Result.Success();
    }
}
