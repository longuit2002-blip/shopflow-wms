using MediatR;
using ShopFlow.Outbound.Application.Ports;

namespace ShopFlow.Outbound.Application.Queries;

/// <summary>
/// MediatR handler for <see cref="GetOrderTransitionsQuery"/> — Sprint-7 plan
/// U3 / R15. Thin wrapper over
/// <see cref="IOrderTransitionRepository.ListByOrderIdAsync"/>; the
/// repository already orders by <c>OccurredAt</c> ASC and the audit row
/// shape lines up 1:1 with the read model.
/// </summary>
/// <remarks>
/// Empty list when the order has no transitions yet (e.g., freshly created
/// before the saga has consumed its first event). The handler does NOT
/// 404 on "unknown" order ids — the transitions log is intentionally
/// independent of the orders table (Sprint-7 R14: the audit is the source
/// of truth even if the order row is later archived).
/// </remarks>
public sealed class GetOrderTransitionsHandler
    : IRequestHandler<GetOrderTransitionsQuery, IReadOnlyList<OrderTransitionReadModel>>
{
    private readonly IOrderTransitionRepository _transitionRepo;

    public GetOrderTransitionsHandler(IOrderTransitionRepository transitionRepo)
    {
        _transitionRepo = transitionRepo;
    }

    public async Task<IReadOnlyList<OrderTransitionReadModel>> Handle(
        GetOrderTransitionsQuery request,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(request);

        var rows = await _transitionRepo
            .ListByOrderIdAsync(request.OrderId, cancellationToken)
            .ConfigureAwait(false);

        var models = new List<OrderTransitionReadModel>(rows.Count);
        foreach (var row in rows)
        {
            models.Add(
                new OrderTransitionReadModel(
                    Id: row.Id,
                    OrderId: row.OrderId,
                    FromState: row.FromState,
                    ToState: row.ToState,
                    OccurredAt: row.OccurredAt,
                    EventType: row.EventType,
                    CorrelationId: row.CorrelationId
                )
            );
        }

        return models;
    }
}
