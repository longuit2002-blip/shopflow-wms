namespace ShopFlow.Inbound.Application.Ports;

/// <summary>
/// Transactional boundary for Inbound writes. Bundles repository writes plus
/// the outbox row that <c>ShopFlow.SharedKernel.Infrastructure.OutboxInterceptor</c>
/// emits. Implementation wraps the per-request <see cref="ShopFlow.Inbound.Infrastructure.InboundDbContext"/>.
/// </summary>
public interface IUnitOfWork
{
    Task<int> SaveChangesAsync(CancellationToken ct);
}
