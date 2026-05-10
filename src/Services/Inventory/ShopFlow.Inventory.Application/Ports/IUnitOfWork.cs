namespace ShopFlow.Inventory.Application.Ports;

/// <summary>
/// Thin abstraction for committing a unit-of-work to persistence. The
/// Application layer never depends on EF directly (per AGENTS.md §2.7);
/// the Infrastructure adapter wraps <c>DbContext.SaveChangesAsync</c>.
/// </summary>
public interface IUnitOfWork
{
    Task<int> SaveChangesAsync(CancellationToken cancellationToken);
}
