using MediatR;

namespace ShopFlow.Outbound.Application.Queries;

/// <summary>
/// MediatR query for <c>GET /api/outbound/orders/{id}/transitions</c> per
/// Sprint-7 plan U3 / R15. Returns all <c>outbound_saga_transitions</c> rows
/// for one order in <c>occurred_at</c> ASC order so the detail page's
/// transitions log renders top-to-bottom chronologically. Empty list when
/// the saga has not produced any transitions yet (e.g., freshly seeded
/// order before the first <c>OrderPlacedV1</c> consume).
/// </summary>
public sealed record GetOrderTransitionsQuery(Guid OrderId) : IRequest<IReadOnlyList<OrderTransitionReadModel>>;

/// <summary>
/// Read model for one row in the transitions log. Sprint-7 R14 — every
/// <see cref="CorrelationId"/> propagates W3C TraceContext per AGENTS.md
/// §6.43, so the frontend can hyperlink the row into the trace explorer.
/// </summary>
/// <param name="Id">Audit row id.</param>
/// <param name="OrderId">Owning order id.</param>
/// <param name="FromState">Saga state pre-transition.</param>
/// <param name="ToState">Saga state post-transition.</param>
/// <param name="OccurredAt">Wall-clock timestamp of the transition.</param>
/// <param name="EventType">CLR-name of the integration event that triggered the transition.</param>
/// <param name="CorrelationId">W3C TraceContext correlation id captured at write time.</param>
public sealed record OrderTransitionReadModel(
    Guid Id,
    Guid OrderId,
    string FromState,
    string ToState,
    DateTime OccurredAt,
    string EventType,
    string CorrelationId);
