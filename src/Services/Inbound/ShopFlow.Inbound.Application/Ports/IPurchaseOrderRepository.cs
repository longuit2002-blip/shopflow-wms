using ShopFlow.Inbound.Domain;

namespace ShopFlow.Inbound.Application.Ports;

/// <summary>
/// Write + read surface for the <see cref="PurchaseOrder"/> aggregate per
/// Sprint-2-redux plan R1-R3. Reads materialise the aggregate with all
/// child lines so handlers can call state-machine methods directly. Writes
/// flush via <see cref="IUnitOfWork.SaveChangesAsync"/>.
/// </summary>
/// <remarks>
/// Per AGENTS.md §3.16 every EF query passes through a tenant-scoped
/// repository; no raw <c>DbSet</c> access in Application or Api
/// (<c>ShopFlow0001</c> enforces).
/// </remarks>
public interface IPurchaseOrderRepository
{
    Task AddAsync(PurchaseOrder po, CancellationToken ct);

    Task<PurchaseOrder?> FindByIdAsync(Guid id, CancellationToken ct);

    Task<IReadOnlyList<PurchaseOrder>> ListOpenAsync(CancellationToken ct);
}
