using Microsoft.EntityFrameworkCore;
using ShopFlow.Inbound.Application.Ports;
using ShopFlow.Inbound.Domain;

namespace ShopFlow.Inbound.Infrastructure.Repositories;

/// <summary>
/// EF Core implementation of <see cref="IPurchaseOrderRepository"/>. Eager-
/// loads the <see cref="PurchaseOrder.Lines"/> child collection on read
/// so handlers can drive the state machine without N+1 round trips.
/// </summary>
public sealed class PurchaseOrderRepository : IPurchaseOrderRepository
{
    private readonly InboundDbContext _db;

    public PurchaseOrderRepository(InboundDbContext db)
    {
        _db = db;
    }

    public async Task AddAsync(PurchaseOrder po, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(po);
        await _db.PurchaseOrders.AddAsync(po, ct).ConfigureAwait(false);
    }

    public Task<PurchaseOrder?> FindByIdAsync(Guid id, CancellationToken ct)
    {
        return _db.PurchaseOrders.Include(p => p.Lines).FirstOrDefaultAsync(p => p.Id == id, ct);
    }

    public async Task<IReadOnlyList<PurchaseOrder>> ListOpenAsync(CancellationToken ct)
    {
        var rows = await _db
            .PurchaseOrders.Include(p => p.Lines)
            .Where(p =>
                p.Status == PurchaseOrderStatus.Open
                || p.Status == PurchaseOrderStatus.PartiallyReceived
            )
            .OrderBy(p => p.ExpectedDeliveryAt)
            .ToListAsync(ct)
            .ConfigureAwait(false);
        return rows;
    }
}
