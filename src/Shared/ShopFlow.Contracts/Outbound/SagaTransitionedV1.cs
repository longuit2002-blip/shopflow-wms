namespace ShopFlow.Contracts.Outbound;

/// <summary>
/// Cross-module integration event signalling one <c>FulfillmentSaga</c>
/// state transition has just been recorded to the per-tenant
/// <c>outbound_saga_transitions</c> audit table. Sprint-7 R11/R14.
/// </summary>
/// <remarks>
/// <para>Published via the Outbound outbox path
/// (<see cref="ShopFlow.Outbound.Application.Ports.IOutboundOutbox"/>) by
/// the <c>SagaTransitionObserver</c> on every TransitionTo in the
/// <c>FulfillmentSaga</c> state machine. Consumed by the SharedKernel
/// <c>SagaTransitionedRelayConsumer</c> (Sprint-7 U6) which pushes the
/// payload to the tenant-scoped SignalR group as a <c>saga_transitioned</c>
/// hub event for the Orders detail surface to consume.</para>
///
/// <para>Carries the envelope fields required by AGENTS.md §6.42 —
/// <see cref="TenantId"/>, <see cref="CorrelationId"/>, and
/// <see cref="OccurredAt"/>. <see cref="OrderId"/> is the saga's
/// <c>CorrelationId</c> + the Order aggregate's <c>Id</c> (K2 from
/// Sprint-3-redux). <see cref="EventType"/> records the CLR-name of
/// whatever integration event triggered the transition (e.g.,
/// <c>StockReservedV1</c>, <c>PickConfirmed</c>); the frontend renders
/// this verbatim as a small monospace label in Sprint-7's
/// <c>TransitionsLog</c>, with a Sprint-7.5 follow-up to translate the
/// CLR-name to a human-readable label.</para>
///
/// <para>Per the Sprint-7 hub topology decision (single hub-host process):
/// only Outbound.Api hosts the relay consumer; other module APIs do not
/// register it. The Gateway routes <c>/hub</c> to Outbound.Api permanently.</para>
/// </remarks>
public sealed record SagaTransitionedV1(
    Guid TenantId,
    Guid OrderId,
    string FromState,
    string ToState,
    DateTime OccurredAt,
    string EventType,
    string CorrelationId
);
