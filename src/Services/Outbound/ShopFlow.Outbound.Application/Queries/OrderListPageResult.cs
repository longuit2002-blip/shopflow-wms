namespace ShopFlow.Outbound.Application.Queries;

/// <summary>
/// Paged result for <see cref="ListOrdersQuery"/>. Carries the page rows +
/// the total row count (for the pager footer) returned as a single read.
/// </summary>
public sealed record OrderListPageResult(
    IReadOnlyList<OrderListRow> Items,
    int TotalCount);

/// <summary>
/// One row of the Sprint-7 Orders list. Wire shape stays PascalCase per
/// Sprint-6 KTD4. <see cref="Channel"/> is parsed by the handler from
/// <see cref="ChannelExternalOrderId"/>'s prefix (<c>SHOPEE_*</c> →
/// <c>"Shopee"</c>, <c>LAZADA_*</c> → <c>"Lazada"</c>, <c>TIKTOK_*</c> →
/// <c>"TikTok Shop"</c>, else <c>"Direct"</c>); the database does not
/// carry a channel column on <c>orders</c>.
/// </summary>
/// <param name="Id">Order id (uuid).</param>
/// <param name="ChannelExternalOrderId">Channel-side order reference.</param>
/// <param name="Channel">Display label parsed from the channel prefix.</param>
/// <param name="LineCount">Number of <c>order_lines</c> rows.</param>
/// <param name="CurrentSagaState">
/// Current saga state string read from <c>saga_state.CurrentState</c>; null
/// when the saga has not started for this order yet (e.g., the seeded test
/// order before <c>OrderPlacedV1</c> is consumed).
/// </param>
/// <param name="CreatedAt">When <c>orders.created_at</c> was stamped; the UI derives "age" from this.</param>
/// <param name="LastTransitionAt">
/// Max <c>outbound_saga_transitions.occurred_at</c> for this order; null
/// when no transitions have been recorded yet.
/// </param>
public sealed record OrderListRow(
    Guid Id,
    string ChannelExternalOrderId,
    string Channel,
    int LineCount,
    string? CurrentSagaState,
    DateTime CreatedAt,
    DateTime? LastTransitionAt);
