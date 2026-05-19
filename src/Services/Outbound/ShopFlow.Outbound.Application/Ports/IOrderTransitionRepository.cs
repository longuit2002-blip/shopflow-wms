using ShopFlow.Outbound.Domain;

namespace ShopFlow.Outbound.Application.Ports;

/// <summary>
/// Append-only audit surface for saga state transitions. Sprint-7 R14/R15.
/// Writes come from the saga's <c>IStateObserver&lt;FulfillmentSagaState&gt;</c>
/// (one row per TransitionTo, fires uniformly across <c>Then</c> / <c>WhenEnter</c>
/// / <c>If</c> branches). Reads serve the Orders detail route's
/// <c>GET /api/outbound/orders/{id}/transitions</c> endpoint.
/// </summary>
/// <remarks>
/// <para>Per AGENTS.md §3.16 every EF query passes through a tenant-scoped
/// repository. <see cref="AppendAsync"/> adds to the change tracker without
/// flushing — the caller's surrounding transaction (the saga's MT EF
/// repository commit, or an explicit <c>SaveChangesAsync</c> in tests)
/// flushes the row alongside the saga state update. This keeps the audit
/// write atomic with the state transition itself.</para>
/// </remarks>
public interface IOrderTransitionRepository
{
    /// <summary>
    /// Stage an audit row for the next flush. Does NOT call
    /// <c>SaveChangesAsync</c>; the saga's commit (or the test's explicit
    /// save) flushes the row alongside the saga state update.
    /// </summary>
    Task AppendAsync(OrderTransition transition, CancellationToken ct);

    /// <summary>
    /// List all transitions for one order, ordered by
    /// <see cref="OrderTransition.OccurredAt"/> ASC. Empty list when the
    /// order has no recorded transitions yet (e.g., a freshly-created order
    /// before the saga has consumed its first event).
    /// </summary>
    Task<IReadOnlyList<OrderTransition>> ListByOrderIdAsync(
        Guid orderId,
        CancellationToken ct
    );
}
