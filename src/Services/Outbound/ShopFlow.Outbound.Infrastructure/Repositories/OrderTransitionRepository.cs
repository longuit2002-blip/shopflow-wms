using Microsoft.EntityFrameworkCore;
using ShopFlow.Outbound.Application.Ports;
using ShopFlow.Outbound.Domain;

namespace ShopFlow.Outbound.Infrastructure.Repositories;

/// <summary>
/// EF Core implementation of <see cref="IOrderTransitionRepository"/>.
/// Sprint-7 U1. <see cref="AppendAsync"/> tracks the row without flushing
/// so the saga's MT EF repository commit picks it up alongside the saga
/// state update — one transaction, atomic audit + state.
/// </summary>
public sealed class OrderTransitionRepository : IOrderTransitionRepository
{
    private readonly OutboundDbContext _db;

    public OrderTransitionRepository(OutboundDbContext db)
    {
        _db = db;
    }

    public async Task AppendAsync(OrderTransition transition, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(transition);
        await _db.OrderTransitions.AddAsync(transition, ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<OrderTransition>> ListByOrderIdAsync(
        Guid orderId,
        CancellationToken ct
    )
    {
        var rows = await _db
            .OrderTransitions.AsNoTracking()
            .Where(o => o.OrderId == orderId)
            .OrderBy(o => o.OccurredAt)
            .ToListAsync(ct)
            .ConfigureAwait(false);
        return rows;
    }
}
