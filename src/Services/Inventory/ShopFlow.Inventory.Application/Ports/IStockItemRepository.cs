using ShopFlow.Inventory.Application.Queries;
using ShopFlow.Inventory.Domain;

namespace ShopFlow.Inventory.Application.Ports;

/// <summary>
/// Tenant-scoped repository for the <see cref="StockItem"/> aggregate. Per
/// AGENTS.md §3.16 raw <c>DbSet&lt;StockItem&gt;</c> access from
/// <c>Application</c>/<c>Api</c> is forbidden; the analyzer ShopFlow0001
/// enforces it. Reads and writes both go through this port.
/// </summary>
public interface IStockItemRepository
{
    /// <summary>
    /// Load the aggregate for write. The DbContext keeps the entity tracked
    /// so the caller can mutate it and rely on
    /// <c>SaveChangesAsync</c> + <c>OutboxInterceptor</c> to persist domain
    /// events atomically.
    /// </summary>
    Task<StockItem?> LoadBySkuAsync(Guid tenantId, Sku sku, CancellationToken cancellationToken);

    /// <summary>
    /// Read-side projection: joins <c>stock_items</c> with the active rows
    /// in <c>reservations_ledger</c> to compute
    /// <c>available = total − allocated − sum(active reservations)</c>
    /// (Tech Design §7.5).
    /// </summary>
    Task<AvailabilityDto?> GetAvailabilityAsync(
        Guid tenantId,
        Sku sku,
        CancellationToken cancellationToken
    );
}
