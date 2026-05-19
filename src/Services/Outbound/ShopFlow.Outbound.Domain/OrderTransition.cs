using ShopFlow.SharedKernel.Domain;

namespace ShopFlow.Outbound.Domain;

/// <summary>
/// Append-only audit row capturing one <see cref="FulfillmentSagaState"/>
/// state transition. Sprint-7 R14 — written by the saga's
/// <c>IStateObserver&lt;FulfillmentSagaState&gt;</c> on every TransitionTo,
/// regardless of whether the transition fires through a <c>Then</c> chain,
/// <c>WhenEnter</c> activity, or <c>If</c>/<c>IfElse</c> branch. Read by
/// the Sprint-7 Orders detail route's <c>TransitionsLog</c> component via
/// <see cref="ShopFlow.Outbound.Application.Ports.IOrderTransitionRepository.ListByOrderIdAsync"/>.
/// </summary>
/// <remarks>
/// <para>Inherits <see cref="BaseEntity"/> (matches <c>Order</c> precedent;
/// EF tracks <c>Id</c> + <c>CreatedAt</c> + <c>UpdatedAt</c> automatically).
/// No <c>tenant_id</c> column per ADR-0003 — the database identity is the
/// tenant boundary.</para>
///
/// <para><see cref="OccurredAt"/> is the wall-clock timestamp of the saga
/// transition (sourced from <c>BehaviorContext.SentTime</c> when available,
/// <see cref="DateTime.UtcNow"/> otherwise). <see cref="EventType"/> records
/// the CLR-name of the integration event that triggered the transition
/// (e.g., <c>StockReservedV1</c>); the frontend renders this verbatim as a
/// small monospace label in Sprint-7 with a Sprint-7.5 follow-up to
/// translate to a human label.</para>
///
/// <para><see cref="CorrelationId"/> propagates W3C TraceContext per
/// AGENTS.md §6.43 — sourced from <c>BehaviorContext.CorrelationId</c> /
/// <c>Activity.Current?.Id</c> at write time so the audit row, the
/// <c>SagaTransitionedV1</c> integration event, and the SignalR hub
/// payload all share one correlation key.</para>
///
/// <para>The row is independent of the <c>orders</c> table — there is no
/// foreign key on <see cref="OrderId"/>. The audit is the source of truth
/// even if the order row is later archived/deleted (defensive; Sprint-7
/// does not delete orders).</para>
/// </remarks>
public sealed class OrderTransition : BaseEntity
{
    public Guid OrderId { get; private set; }

    public string FromState { get; private set; } = string.Empty;

    public string ToState { get; private set; } = string.Empty;

    public DateTime OccurredAt { get; private set; }

    public string EventType { get; private set; } = string.Empty;

    public string CorrelationId { get; private set; } = string.Empty;

    private OrderTransition() { }

    /// <summary>
    /// Build an audit row. All fields required; the caller (the saga
    /// state observer) supplies fully-resolved values.
    /// </summary>
    public static OrderTransition Create(
        Guid orderId,
        string fromState,
        string toState,
        DateTime occurredAt,
        string eventType,
        string correlationId
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fromState);
        ArgumentException.ThrowIfNullOrWhiteSpace(toState);
        ArgumentException.ThrowIfNullOrWhiteSpace(eventType);
        ArgumentException.ThrowIfNullOrWhiteSpace(correlationId);

        if (orderId == Guid.Empty)
        {
            throw new ArgumentException("OrderId must not be empty.", nameof(orderId));
        }

        return new OrderTransition
        {
            OrderId = orderId,
            FromState = fromState,
            ToState = toState,
            OccurredAt = occurredAt,
            EventType = eventType,
            CorrelationId = correlationId,
        };
    }
}
