using Microsoft.EntityFrameworkCore;
using ShopFlow.Inventory.Application.Ports;
using ShopFlow.Inventory.Application.Queries;
using ShopFlow.Inventory.Domain;

namespace ShopFlow.Inventory.Infrastructure.Repositories;

/// <summary>
/// Tenant-scoped repository for the <see cref="StockItem"/> aggregate.
/// Reads are filtered by the global query filter on the DbContext; writes
/// rely on EF change tracking + the kernel's <c>OutboxInterceptor</c> for
/// domain-event flush.
/// </summary>
public sealed class StockItemRepository : IStockItemRepository
{
    private readonly InventoryDbContext _db;
    private readonly TimeProvider _clock;

    public StockItemRepository(InventoryDbContext db, TimeProvider clock)
    {
        _db = db;
        _clock = clock;
    }

    public Task<StockItem?> LoadBySkuAsync(
        Guid tenantId,
        Sku sku,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(sku);
        var skuValue = sku.Value;
        return _db.StockItems.FirstOrDefaultAsync(
            s => s.TenantId == tenantId && s.Sku == skuValue,
            cancellationToken
        );
    }

    public async Task<AvailabilityDto?> GetAvailabilityAsync(
        Guid tenantId,
        Sku sku,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(sku);
        var skuValue = sku.Value;
        var nowUtc = _clock.GetUtcNow().UtcDateTime;

        var stockItem = await _db
            .StockItems.AsNoTracking()
            .FirstOrDefaultAsync(
                s => s.TenantId == tenantId && s.Sku == skuValue,
                cancellationToken
            )
            .ConfigureAwait(false);

        if (stockItem is null)
        {
            return null;
        }

        var activeReservedQty = await _db
            .Reservations.AsNoTracking()
            .Where(r =>
                r.TenantId == tenantId
                && r.Sku == skuValue
                && r.Status == ReservationStatus.Active
                && r.ExpiresAt > nowUtc
            )
            .SumAsync(r => (int?)r.Qty, cancellationToken)
            .ConfigureAwait(false);

        var activeReserved = activeReservedQty ?? 0;
        var available = Math.Max(
            0,
            stockItem.TotalQuantity - stockItem.AllocatedQuantity - activeReserved
        );

        return new AvailabilityDto(
            Sku: skuValue,
            TotalQuantity: stockItem.TotalQuantity,
            AllocatedQuantity: stockItem.AllocatedQuantity,
            ActiveReservationQuantity: activeReserved,
            AvailableQuantity: available
        );
    }
}
