using ShopFlow.Inventory.Application.Ports;
using ShopFlow.Inventory.Domain;
using ShopFlow.SharedKernel.Domain;

namespace ShopFlow.Inventory.Infrastructure.Repositories;

/// <summary>
/// EF Core implementation of <see cref="IStockItemRepository"/>. U8 ships
/// the skeleton; Sprint-1-redux (plan 003) implements the bodies against
/// the conditional-INSERT pattern and the in-transaction outbox emission.
/// </summary>
/// <remarks>
/// Per the pattern documented at
/// <c>docs/solutions/2026-05-10-green-against-stub-property-suite.md</c>:
/// integration and property tests for the reservation ledger fail with
/// <see cref="NotImplementedException"/> against this stub. That is the
/// W1 green-against-stub state — Sprint-1-redux makes them green by
/// implementing behavior, not by stubbing the assertions.
/// </remarks>
public sealed class StockItemRepository : IStockItemRepository
{
    private readonly InventoryDbContext _db;

    public StockItemRepository(InventoryDbContext db)
    {
        _db = db;
    }

    public Task<StockItem?> FindBySkuAsync(Sku sku, CancellationToken ct)
    {
        _ = (sku, ct, _db);
        throw new NotImplementedException(
            "Sprint-1-redux behavior — see docs/plans/2026-05-11-003-phase-1-sprint-1-redux-reservation-ledger-plan.md"
        );
    }

    public Task AddAsync(StockItem item, CancellationToken ct)
    {
        _ = (item, ct);
        throw new NotImplementedException(
            "Sprint-1-redux behavior — see docs/plans/2026-05-11-003-phase-1-sprint-1-redux-reservation-ledger-plan.md"
        );
    }

    public Task<Result> AdjustAsync(
        Sku sku,
        int delta,
        StockAdjustmentReason reason,
        string? note,
        CancellationToken ct
    )
    {
        _ = (sku, delta, reason, note, ct);
        throw new NotImplementedException(
            "Sprint-1-redux behavior — see docs/plans/2026-05-11-003-phase-1-sprint-1-redux-reservation-ledger-plan.md"
        );
    }
}
