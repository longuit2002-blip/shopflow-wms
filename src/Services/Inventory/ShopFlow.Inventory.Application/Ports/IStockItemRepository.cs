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
    /// Sprint-3-redux body — currently NIE.
    /// </summary>
    Task<Result> AdjustAsync(
        Sku sku,
        int delta,
        StockAdjustmentReason reason,
        string? note,
        CancellationToken ct
    );

    /// <summary>
    /// Apply a bin-targeted stock adjustment per Sprint-2-redux plan
    /// R13-R15. UPSERTs <c>stock_items</c> for unknown SKU
    /// (<c>available=0, reserved=0</c>) idempotently, UPSERTs
    /// <c>stock_item_bins (sku, bin_id)</c> with the delta, updates
    /// <c>stock_items.available</c>, and INSERTs a
    /// <c>stock_adjustments</c> audit row. All atomic in one tenant
    /// transaction. Returns failure on bin underflow (negative delta
    /// exceeding current bin quantity).
    /// </summary>
    Task<Result> AdjustAtBinAsync(
        Sku sku,
        long binId,
        int delta,
        StockAdjustmentReason reason,
        string? note,
        CancellationToken ct
    );
}
