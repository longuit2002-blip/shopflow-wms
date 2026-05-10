using ShopFlow.Inventory.Domain;
using ShopFlow.SharedKernel.Domain;

namespace ShopFlow.Inventory.Application.Ports;

/// <summary>
/// Tenant-scoped repository for the reservation ledger
/// (<c>reservations_ledger</c>). The hot-path method
/// <see cref="TryReserveAsync"/> implements the conditional INSERT CTE from
/// Tech Design §7.2 verbatim — see
/// <c>Infrastructure/Repositories/ReservationRepository.cs</c>.
/// </summary>
public interface IReservationRepository
{
    /// <summary>
    /// Attempt to append a reservation row using the §7.2 CTE.
    /// </summary>
    /// <returns>
    /// <see cref="Result{T}.Success"/> with the inserted reservation id when
    /// the conditional INSERT writes a row; <see cref="Result{T}.Failure"/>
    /// with code <c>"OVERSOLD"</c> when zero rows are written (insufficient
    /// available quantity).
    /// </returns>
    Task<Result<Guid>> TryReserveAsync(
        Guid tenantId,
        Sku sku,
        int qty,
        Guid orderId,
        CancellationToken cancellationToken
    );

    /// <summary>
    /// Idempotency lookup keyed on <c>(tenant_id, order_id)</c>; returns the
    /// existing reservation row if any, or <c>null</c> for a first-write call.
    /// Per Tech Design §7.7 the application handler short-circuits on an
    /// existing row before attempting another insert.
    /// </summary>
    Task<Reservation?> FindByOrderIdAsync(
        Guid tenantId,
        Guid orderId,
        CancellationToken cancellationToken
    );

    /// <summary>
    /// Background-worker entry point: flips every active reservation whose
    /// <c>expires_at &lt; NOW()</c> to <see cref="ReservationStatus.Expired"/>
    /// and returns the number of rows affected. Per Tech Design §7.4 each
    /// expired row also emits a <c>StockReleased</c> event via the outbox.
    /// </summary>
    Task<int> ReleaseExpiredAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Confirm an active reservation: transitions status to
    /// <see cref="ReservationStatus.Confirmed"/> and stamps
    /// <c>finalized_at</c>. The corresponding <c>stock_items.total_qty</c>
    /// deduction is performed by <see cref="StockItem.ConfirmDeduction"/>
    /// in the same transaction.
    /// </summary>
    Task ConfirmAsync(Guid reservationId, CancellationToken cancellationToken);
}
