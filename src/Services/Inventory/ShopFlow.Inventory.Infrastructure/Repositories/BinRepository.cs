using Microsoft.EntityFrameworkCore;
using ShopFlow.Inventory.Application.Ports;
using ShopFlow.Inventory.Domain;

namespace ShopFlow.Inventory.Infrastructure.Repositories;

public sealed class BinRepository : IBinRepository
{
    private readonly InventoryDbContext _db;

    public BinRepository(InventoryDbContext db)
    {
        _db = db;
    }

    public Task<Bin?> FindByIdAsync(long binId, CancellationToken ct) =>
        _db.Bins.FirstOrDefaultAsync(b => b.BinId == binId, ct);

    public async Task<IReadOnlyList<Bin>> ListByZoneAsync(long zoneId, CancellationToken ct)
    {
        var rows = await _db
            .Bins.AsNoTracking()
            .Where(b => b.ZoneId == zoneId)
            .OrderBy(b => b.Name)
            .ToListAsync(ct)
            .ConfigureAwait(false);
        return rows;
    }
}
