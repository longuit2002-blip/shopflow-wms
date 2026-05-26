using ShopFlow.Outbound.Application.Queries;
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
///
/// <para>Sprint-7 U3 adds the read-side <see cref="ListAsync"/> +
/// <see cref="GetCurrentSagaStateAsync"/> surfaces consumed by the
/// MediatR query handlers behind the Orders screen. <see cref="ListAsync"/>
/// joins <c>outbound_saga_transitions</c> (max <c>occurred_at</c> per
/// order) in a single grouped query so the list endpoint avoids N+1 hits
/// on the audit table. <see cref="GetCurrentSagaStateAsync"/> reads the
/// MassTransit <c>saga_state</c> table — there is no aggregate root for
/// saga state, so the repository surfaces the raw current-state string.</para>
/// </remarks>
public interface IOrderRepository
{
    Task AddAsync(Order order, CancellationToken ct);

    Task<Order?> FindByIdAsync(Guid id, CancellationToken ct);

    Task<Order?> FindByExternalIdAsync(string channelExternalOrderId, CancellationToken ct);

    /// <summary>
    /// Paged, filtered listing for the Sprint-7 Orders screen. Returns the
    /// page rows + total-count in one tracked query. The
    /// <see cref="OrderListRow.LastTransitionAt"/> column is sourced from
    /// <c>outbound_saga_transitions</c> via a per-order MAX join so the
    /// list endpoint stays single-trip per page.
    /// </summary>
    /// <param name="filter">Status / channel-prefix / search / since / until knobs. All optional.</param>
    /// <param name="skip">Offset for paging; non-negative.</param>
    /// <param name="take">Page size; clamped by the caller.</param>
    /// <param name="ct">Cancellation token.</param>
    Task<OrderListPageResult> ListAsync(
        OrderListFilter filter,
        int skip,
        int take,
        CancellationToken ct
    );

    /// <summary>
    /// Read the saga's <c>CurrentState</c> for one order. Returns
    /// <see langword="null"/> when no saga row exists yet (e.g., a freshly
    /// created order whose <c>OrderPlacedV1</c> hasn't been consumed).
    /// </summary>
    Task<string?> GetCurrentSagaStateAsync(Guid orderId, CancellationToken ct);
}
