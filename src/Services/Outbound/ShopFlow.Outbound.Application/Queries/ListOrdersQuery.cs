using MediatR;

namespace ShopFlow.Outbound.Application.Queries;

/// <summary>
/// MediatR query for the Sprint-7 Orders list screen (plan U3, R2). Paginated,
/// filterable read over the <c>orders</c> table with the latest
/// <c>outbound_saga_transitions.occurred_at</c> joined per order so the
/// "Last update" column renders without N+1 round trips.
/// </summary>
/// <remarks>
/// <para>All filter fields are optional. Empty / null filter returns every
/// order in the tenant's database (clamped by paging). The
/// <see cref="OrderListFilter.ChannelPrefix"/> filter is a case-sensitive
/// prefix match on <c>channel_external_order_id</c> — Sprint-7's marketplace
/// taxonomy uses upper-case prefixes (<c>SHOPEE_*</c>, <c>LAZADA_*</c>,
/// <c>TIKTOK_*</c>); anything else maps to "Direct" client-side.</para>
///
/// <para>Per Sprint-6 KTD4 the wire shape stays PascalCase (.NET default
/// serializer); the U4 controller does not need additional shaping.</para>
/// </remarks>
public sealed record ListOrdersQuery(OrderListFilter Filter, int Skip, int Take)
    : IRequest<OrderListPageResult>;

/// <summary>
/// Filter knobs for <see cref="ListOrdersQuery"/>. All optional; the handler
/// builds the EF query incrementally so missing knobs erase to "match all".
/// </summary>
/// <param name="Status">Optional <c>orders.status</c> equality (case-sensitive enum name, e.g. <c>"Reserved"</c>).</param>
/// <param name="ChannelPrefix">Optional case-sensitive prefix match on <c>channel_external_order_id</c>.</param>
/// <param name="Search">Optional case-insensitive substring match on <c>channel_external_order_id</c>.</param>
/// <param name="Since">Optional lower bound on <c>orders.created_at</c> (inclusive).</param>
/// <param name="Until">Optional upper bound on <c>orders.created_at</c> (inclusive).</param>
public sealed record OrderListFilter(
    string? Status = null,
    string? ChannelPrefix = null,
    string? Search = null,
    DateTime? Since = null,
    DateTime? Until = null
);
