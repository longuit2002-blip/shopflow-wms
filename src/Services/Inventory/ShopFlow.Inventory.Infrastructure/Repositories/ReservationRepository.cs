using ShopFlow.Inventory.Application.Ports;
using ShopFlow.Inventory.Domain;
using ShopFlow.SharedKernel.Domain;

namespace ShopFlow.Inventory.Infrastructure.Repositories;

/// <summary>
/// EF Core + raw-SQL implementation of <see cref="IReservationRepository"/>.
/// Sprint-1-redux (plan 003) implements the conditional-INSERT CTE
/// (READ COMMITTED) per Tech Design v3.0 §4.4 plus the
/// <c>UNIQUE(order_id)</c> idempotency anchor. U8 ships the skeleton so
/// the property suite for the ledger spec is wired and fails red.
/// </summary>
public sealed class ReservationRepository : IReservationRepository
{
    private readonly InventoryDbContext _db;

    public ReservationRepository(InventoryDbContext db)
    {
        _db = db;
    }

    public Task<Result<Reservation>> TryReserveAsync(
        Sku sku,
        string orderId,
        Quantity quantity,
        TimeSpan ttl,
        CancellationToken ct
    )
    {
        _ = (sku, orderId, quantity, ttl, ct, _db);
        throw new NotImplementedException(
            "Sprint-1-redux behavior — see docs/plans/2026-05-11-003-phase-1-sprint-1-redux-reservation-ledger-plan.md"
        );
    }

    public Task<Reservation?> FindByOrderIdAsync(string orderId, CancellationToken ct)
    {
        _ = (orderId, ct);
        throw new NotImplementedException(
            "Sprint-1-redux behavior — see docs/plans/2026-05-11-003-phase-1-sprint-1-redux-reservation-ledger-plan.md"
        );
    }

    public Task<Result> ConfirmAsync(string orderId, CancellationToken ct)
    {
        _ = (orderId, ct);
        throw new NotImplementedException(
            "Sprint-1-redux behavior — see docs/plans/2026-05-11-003-phase-1-sprint-1-redux-reservation-ledger-plan.md"
        );
    }

    public Task<Result> ReleaseAsync(string orderId, CancellationToken ct)
    {
        _ = (orderId, ct);
        throw new NotImplementedException(
            "Sprint-1-redux behavior — see docs/plans/2026-05-11-003-phase-1-sprint-1-redux-reservation-ledger-plan.md"
        );
    }

    public Task<int> ReleaseExpiredAsync(DateTime now, int batchSize, CancellationToken ct)
    {
        _ = (now, batchSize, ct);
        throw new NotImplementedException(
            "Sprint-1-redux behavior — see docs/plans/2026-05-11-003-phase-1-sprint-1-redux-reservation-ledger-plan.md"
        );
    }
}
