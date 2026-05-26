using MediatR;
using ShopFlow.Outbound.Application.Ports;

namespace ShopFlow.Outbound.Application.Queries;

/// <summary>
/// MediatR handler for <see cref="ListOrdersQuery"/> — Sprint-7 plan U3 / R2.
/// Delegates the join-heavy read (orders + saga_state + max
/// outbound_saga_transitions.occurred_at) to
/// <see cref="IOrderRepository.ListAsync"/>; this keeps repository-mediated
/// EF access per AGENTS.md §3.16 and lets the unit tests stub the read with
/// NSubstitute. The channel-display label is parsed from the
/// <c>channel_external_order_id</c> prefix in <see cref="ParseChannel"/>.
/// </summary>
/// <remarks>
/// <para>The handler clamps <see cref="ListOrdersQuery.Take"/> to a defensive
/// upper bound (200) and pins <see cref="ListOrdersQuery.Skip"/> at zero or
/// above. Pagination beyond the clamp is intentional input — the controller
/// in U4 surfaces 400 for negative values; this handler ensures a slipped
/// negative cannot crash the SQL plan.</para>
///
/// <para><see cref="OrderListFilter.ChannelPrefix"/> is forwarded verbatim to
/// the repository (case-sensitive). The plan's display-label parsing applies
/// to whatever <c>channel_external_order_id</c> rows come back — even when
/// <see cref="OrderListFilter.ChannelPrefix"/> is null/empty the
/// <see cref="ParseChannel"/> helper labels every row consistently.</para>
/// </remarks>
public sealed class ListOrdersHandler : IRequestHandler<ListOrdersQuery, OrderListPageResult>
{
    /// <summary>
    /// Defensive upper bound on the paging window. Sprint-7's Orders screen
    /// renders 50 rows per page by default; 200 is the safety ceiling so the
    /// list endpoint cannot be coerced into materialising the entire orders
    /// table in one trip.
    /// </summary>
    public const int MaxTake = 200;

    private readonly IOrderRepository _orderRepo;

    public ListOrdersHandler(IOrderRepository orderRepo)
    {
        _orderRepo = orderRepo;
    }

    public async Task<OrderListPageResult> Handle(
        ListOrdersQuery request,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(request);

        var skip = Math.Max(0, request.Skip);
        var take = Math.Clamp(request.Take, 1, MaxTake);

        var page = await _orderRepo
            .ListAsync(request.Filter, skip, take, cancellationToken)
            .ConfigureAwait(false);

        // The repository surfaces the raw channel external order id; the
        // display label is a handler concern (Sprint-7 plan §U3 Patterns).
        // Re-project the rows so each carries the parsed Channel label even
        // when the repository did the parsing-blind read.
        var items = new List<OrderListRow>(page.Items.Count);
        foreach (var row in page.Items)
        {
            items.Add(row with { Channel = ParseChannel(row.ChannelExternalOrderId) });
        }

        return new OrderListPageResult(items, page.TotalCount);
    }

    /// <summary>
    /// Map a <c>channel_external_order_id</c> to its human display label per
    /// the Sprint-7 doc-review channel-prefix taxonomy.
    /// </summary>
    internal static string ParseChannel(string channelExternalOrderId)
    {
        if (string.IsNullOrEmpty(channelExternalOrderId))
        {
            return "Direct";
        }
        if (channelExternalOrderId.StartsWith("SHOPEE_", StringComparison.Ordinal))
        {
            return "Shopee";
        }
        if (channelExternalOrderId.StartsWith("LAZADA_", StringComparison.Ordinal))
        {
            return "Lazada";
        }
        if (channelExternalOrderId.StartsWith("TIKTOK_", StringComparison.Ordinal))
        {
            return "TikTok Shop";
        }
        return "Direct";
    }
}
