namespace ShopFlow.Outbound.Application.Ports;

/// <summary>
/// Transactional boundary for Outbound writes. Bundles repository writes
/// plus the outbox row that <see cref="IOutboundOutbox"/> enqueues — one
/// <see cref="SaveChangesAsync"/> commits the aggregate insert/update
/// + the outbox row atomically. Implementation wraps the per-request
/// <c>ShopFlow.Outbound.Infrastructure.OutboundDbContext</c>.
/// </summary>
public interface IUnitOfWork
{
    Task<int> SaveChangesAsync(CancellationToken ct);
}
