using ShopFlow.Inventory.Application.Ports;

namespace ShopFlow.Inventory.Infrastructure.Repositories;

/// <summary>
/// One-line wrapper that exposes <see cref="InventoryDbContext.SaveChangesAsync"/>
/// as <see cref="IUnitOfWork.SaveChangesAsync"/>. Lets handlers commit the
/// transaction without taking a direct dependency on EF Core (AGENTS.md §3.16).
/// </summary>
public sealed class InventoryUnitOfWork : IUnitOfWork
{
    private readonly InventoryDbContext _db;

    public InventoryUnitOfWork(InventoryDbContext db)
    {
        _db = db;
    }

    public Task<int> SaveChangesAsync(CancellationToken ct) => _db.SaveChangesAsync(ct);
}
