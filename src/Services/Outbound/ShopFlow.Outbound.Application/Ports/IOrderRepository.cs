using ShopFlow.Outbound.Domain;

namespace ShopFlow.Outbound.Application.Ports;

/// <summary>
/// Write + read surface for the <see cref="Order"/> aggregate per
/// Sprint-3-redux plan R1-R3. Reads materialise the aggregate with all
/// child <see cref="OrderLine"/>s so handlers + the saga can drive
/// state-machine methods directly. Writes flush via
/// <see cref="IUnitOfWork.SaveChangesAsync"/>.
/// </summary>
/// <remarks>
/// <para>Per AGENTS.md §3.16 every EF query passes through a tenant-scoped
/// repository; no raw <c>DbSet</c> access in Application or Api
/// (<c>ShopFlow0001</c> enforces).</para>
///
/// <para><see cref="FindByExternalIdAsync"/> is the idempotency anchor
/// for <c>POST /api/outbound/orders</c>: same
/// <c>channel_external_order_id</c> twice returns the same order id
/// rather than creating a duplicate. Backed by the
/// <c>UNIQUE(channel_external_order_id)</c> index (plan R1) — defence
/// in depth: the index catches a race where two POSTs slip past the
/// short-circuit at the same instant.</para>
/// </remarks>
public interface IOrderRepository
{
    Task AddAsync(Order order, CancellationToken ct);

    Task<Order?> FindByIdAsync(Guid id, CancellationToken ct);

    Task<Order?> FindByExternalIdAsync(string channelExternalOrderId, CancellationToken ct);
}
