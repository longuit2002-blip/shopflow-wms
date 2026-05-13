using Microsoft.EntityFrameworkCore;
using ShopFlow.Outbound.Application.Ports;
using ShopFlow.Outbound.Domain;

namespace ShopFlow.Outbound.Infrastructure.Repositories;

/// <summary>
/// EF Core implementation of <see cref="IOrderRepository"/>. Eager-loads
/// the <see cref="Order.Lines"/> child collection on read so the saga +
/// HTTP responses can materialise the aggregate without N+1 round trips.
/// </summary>
public sealed class OrderRepository : IOrderRepository
{
    private readonly OutboundDbContext _db;

    public OrderRepository(OutboundDbContext db)
    {
        _db = db;
    }

    public async Task AddAsync(Order order, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(order);
        await _db.Orders.AddAsync(order, ct).ConfigureAwait(false);
    }

    public Task<Order?> FindByIdAsync(Guid id, CancellationToken ct)
    {
        return _db.Orders.Include(o => o.Lines).FirstOrDefaultAsync(o => o.Id == id, ct);
    }

    public Task<Order?> FindByExternalIdAsync(
        string channelExternalOrderId,
        CancellationToken ct
    )
    {
        return _db
            .Orders.Include(o => o.Lines)
            .FirstOrDefaultAsync(o => o.ChannelExternalOrderId == channelExternalOrderId, ct);
    }
}
