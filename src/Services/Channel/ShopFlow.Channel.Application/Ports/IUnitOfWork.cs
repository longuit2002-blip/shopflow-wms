namespace ShopFlow.Channel.Application.Ports;

/// <summary>
/// Transactional boundary for Channel writes. Bundles repository writes plus
/// the outbox row that <see cref="IChannelOutbox"/> enqueues — one
/// <see cref="SaveChangesAsync"/> commits the <c>webhook_events</c> insert
/// + the <c>channel_outbox_messages</c> row atomically. Implementation
/// wraps the per-request <c>ChannelDbContext</c>.
/// </summary>
public interface IUnitOfWork
{
    Task<int> SaveChangesAsync(CancellationToken ct);
}
