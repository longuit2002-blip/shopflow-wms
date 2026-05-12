namespace ShopFlow.Inventory.Application.Ports;

/// <summary>
/// Transactional boundary for Inventory writes. Bundles
/// <see cref="IStockItemRepository"/> and <see cref="IReservationRepository"/>
/// writes plus the outbox row that the
/// <c>ShopFlow.SharedKernel.Infrastructure.OutboxInterceptor</c> emits.
/// </summary>
/// <remarks>
/// Implementation wraps a single <c>InventoryDbContext</c> obtained from
/// <c>IDbContextFactory&lt;InventoryDbContext&gt;</c> (AGENTS.md §3.17).
/// <see cref="SaveChangesAsync"/> commits the ambient transaction and
/// returns the rows-affected count; callers should not invoke
/// <c>DbContext.SaveChangesAsync</c> directly through the repository
/// implementations.
/// </remarks>
public interface IUnitOfWork
{
    Task<int> SaveChangesAsync(CancellationToken ct);
}
