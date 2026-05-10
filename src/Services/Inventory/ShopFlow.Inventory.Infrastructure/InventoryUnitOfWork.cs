using ShopFlow.Inventory.Application.Ports;

namespace ShopFlow.Inventory.Infrastructure;

/// <summary>
/// Adapter mapping <see cref="IUnitOfWork.SaveChangesAsync"/> onto the
/// module's <see cref="InventoryDbContext"/>. The kernel's
/// <c>OutboxInterceptor</c> + <c>TenancyInterceptor</c> run inside this
/// SaveChanges, atomic with the business write.
/// </summary>
public sealed class InventoryUnitOfWork : IUnitOfWork
{
    private readonly InventoryDbContext _db;

    public InventoryUnitOfWork(InventoryDbContext db)
    {
        _db = db;
    }

    public Task<int> SaveChangesAsync(CancellationToken cancellationToken) =>
        _db.SaveChangesAsync(cancellationToken);
}
