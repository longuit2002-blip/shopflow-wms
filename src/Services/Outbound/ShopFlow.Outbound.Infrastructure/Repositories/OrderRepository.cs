using Microsoft.EntityFrameworkCore;
using ShopFlow.Outbound.Application.Ports;
using ShopFlow.Outbound.Application.Queries;
using ShopFlow.Outbound.Application.Sagas;
using ShopFlow.Outbound.Domain;

namespace ShopFlow.Outbound.Infrastructure.Repositories;

/// <summary>
/// EF Core implementation of <see cref="IOrderRepository"/>. Eager-loads
/// the <see cref="Order.Lines"/> child collection on read so the saga +
/// HTTP responses can materialise the aggregate without N+1 round trips.
/// </summary>
/// <remarks>
/// <para>Sprint-7 U3 adds the read-side <see cref="ListAsync"/> +
/// <see cref="GetCurrentSagaStateAsync"/> implementations consumed by the
/// MediatR query handlers behind the Orders screen. <see cref="ListAsync"/>
/// joins <c>outbound_saga_transitions</c> on the
/// <c>ix_outbound_saga_transitions_order_occurred</c> index — single-trip
/// MAX-per-order projection avoids N+1 against the audit table.</para>
///
/// <para><see cref="GetCurrentSagaStateAsync"/> reads MassTransit's
/// <c>saga_state</c> table via <c>DbContext.Set&lt;FulfillmentSagaState&gt;()</c>;
/// the EF model registers the entity (see
/// <c>FulfillmentSagaStateConfiguration</c>) but does not expose a
/// <c>DbSet</c> on <see cref="OutboundDbContext"/> because MT's EF saga
/// repository owns the write path. Reads are still allowed; the
/// <c>AsNoTracking</c> + projection keeps EF from racing the saga repo for
/// the entity's tracked instance.</para>
/// </remarks>
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

    public async Task<OrderListPageResult> ListAsync(
        OrderListFilter filter,
        int skip,
        int take,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(filter);

        IQueryable<Order> q = _db.Orders.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(filter.Status))
        {
            // Status is mapped via HasConversion<string>() in OrderConfiguration,
            // so direct equality on the enum value with the string-converted
            // column works through EF's translator. Parse defensively.
            if (Enum.TryParse<OrderStatus>(filter.Status, ignoreCase: false, out var status))
            {
                q = q.Where(o => o.Status == status);
            }
            else
            {
                // Unknown status string — return empty page rather than
                // surfacing all rows.
                return new OrderListPageResult(Array.Empty<OrderListRow>(), 0);
            }
        }

        if (!string.IsNullOrEmpty(filter.ChannelPrefix))
        {
            var prefix = filter.ChannelPrefix;
            q = q.Where(o => o.ChannelExternalOrderId.StartsWith(prefix));
        }

        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            var search = filter.Search.Trim();
            q = q.Where(o => EF.Functions.ILike(o.ChannelExternalOrderId, $"%{search}%"));
        }

        if (filter.Since is DateTime since)
        {
            q = q.Where(o => o.CreatedAt >= since);
        }

        if (filter.Until is DateTime until)
        {
            q = q.Where(o => o.CreatedAt <= until);
        }

        var total = await q.CountAsync(ct).ConfigureAwait(false);

        // Single-trip read with the LastTransitionAt join. Group-by-projection
        // pulls the MAX(occurred_at) per order from outbound_saga_transitions
        // alongside the order row + line count.
        var transitions = _db.OrderTransitions.AsNoTracking();

        var rows = await q
            .OrderByDescending(o => o.CreatedAt)
            .Skip(skip)
            .Take(take)
            .Select(o => new
            {
                Id = o.Id,
                ChannelExternalOrderId = o.ChannelExternalOrderId,
                LineCount = o.Lines.Count,
                Status = o.Status,
                CreatedAt = o.CreatedAt,
                LastTransitionAt = transitions
                    .Where(t => t.OrderId == o.Id)
                    .Max(t => (DateTime?)t.OccurredAt),
            })
            .ToListAsync(ct)
            .ConfigureAwait(false);

        // Look up CurrentSagaState per order in one round-trip via WHERE IN.
        // For the worst-case page (200 rows) this is a single SELECT against
        // the saga_state table's primary key — cheap.
        var orderIds = rows.Select(r => r.Id).ToList();
        Dictionary<Guid, string> sagaStateMap;
        if (orderIds.Count == 0)
        {
            sagaStateMap = new Dictionary<Guid, string>(0);
        }
        else
        {
            sagaStateMap = await _db
                .Set<FulfillmentSagaState>()
                .AsNoTracking()
                .Where(s => orderIds.Contains(s.CorrelationId))
                .Select(s => new { s.CorrelationId, s.CurrentState })
                .ToDictionaryAsync(s => s.CorrelationId, s => s.CurrentState, ct)
                .ConfigureAwait(false);
        }

        var items = rows
            .Select(r => new OrderListRow(
                Id: r.Id,
                ChannelExternalOrderId: r.ChannelExternalOrderId,
                // Channel is parsed by the handler; surface "" here so the
                // repository contract stays parsing-blind and the handler
                // remains the single source of truth for the display label.
                Channel: string.Empty,
                LineCount: r.LineCount,
                CurrentSagaState: sagaStateMap.TryGetValue(r.Id, out var state) ? state : null,
                CreatedAt: r.CreatedAt,
                LastTransitionAt: r.LastTransitionAt))
            .ToList();

        return new OrderListPageResult(items, total);
    }

    public async Task<string?> GetCurrentSagaStateAsync(Guid orderId, CancellationToken ct)
    {
        var row = await _db
            .Set<FulfillmentSagaState>()
            .AsNoTracking()
            .Where(s => s.CorrelationId == orderId)
            .Select(s => s.CurrentState)
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);
        return row;
    }
}
