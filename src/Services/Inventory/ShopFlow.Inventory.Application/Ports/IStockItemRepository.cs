using ShopFlow.Inventory.Domain;
using ShopFlow.SharedKernel.Domain;

namespace ShopFlow.Inventory.Application.Ports;

/// <summary>
/// Read + write surface for <see cref="StockItem"/> aggregates. Reads
/// resolve by SKU (the natural key per Tech Design v3.0 §4.2); writes
/// go through <see cref="IUnitOfWork"/> for transactional grouping with
/// the reservation ledger update.
/// </summary>
/// <remarks>
/// Per AGENTS.md §3.16 every EF query passes through a tenant-scoped
/// repository — no raw <c>DbSet&lt;T&gt;</c> access in Application or
/// Api layers (ShopFlow0001 analyzer enforces). Implementations are
/// constructed via <c>IDbContextFactory&lt;InventoryDbContext&gt;</c> so
/// the tenant DB binding is read from <c>IRequestContext</c> at scope
/// entry.
/// </remarks>
public interface IStockItemRepository
{
    Task<StockItem?> FindBySkuAsync(Sku sku, CancellationToken ct);

    Task AddAsync(StockItem item, CancellationToken ct);

    /// <summary>
    /// Apply a stock adjustment in the same transaction as the available
    /// update. The reason is recorded as a <c>stock_adjustments</c> row.
    /// </summary>
    Task<Result> AdjustAsync(
        Sku sku,
        int delta,
        StockAdjustmentReason reason,
        string? note,
        CancellationToken ct
    );
}
