namespace ShopFlow.SharedKernel.Infrastructure.SignalR;

/// <summary>
/// Sprint-7 plan U6 — wire-shape payloads for the two server → client SignalR
/// hub events emitted by the relay consumers (<see cref="StockChangedRelayConsumer"/>
/// and <see cref="SagaTransitionedRelayConsumer"/>).
/// </summary>
/// <remarks>
/// <para>These payloads are intentionally <em>separate</em> record types from
/// the corresponding cross-module integration events
/// (<c>ShopFlow.Contracts.Inventory.StockLevelChangedV1</c> and
/// <c>ShopFlow.Contracts.Outbound.SagaTransitionedV1</c>). The relay
/// consumer maps the integration event to the hub payload so a future
/// wire-shape change — Sprint-7.5's camelCase normalisation in particular —
/// can land on the hub surface without rippling through every module's
/// consumer.</para>
///
/// <para>Hub event names are the contract surface for the frontend
/// <c>useSignalR</c> hook (shipped in Sprint-7 U7 at <c>web/src/lib/signalr.ts</c>):</para>
/// <list type="bullet">
///   <item><description><c>"stock_changed"</c> → <see cref="StockChangedPayload"/>.</description></item>
///   <item><description><c>"saga_transitioned"</c> → <see cref="SagaTransitionedPayload"/>.</description></item>
/// </list>
///
/// <para>Per AGENTS.md §6.42 every payload carries the envelope triplet
/// <c>tenant_id</c> / <c>correlation_id</c> / <c>occurred_at</c>. The hub
/// payload mirrors the same shape for parity even though SignalR fan-out is
/// not technically a published integration event. <see cref="CorrelationId"/>
/// is populated from the consumer's current <c>Activity.Id</c> per AGENTS.md
/// §6.43 W3C TraceContext propagation.</para>
/// </remarks>
public sealed record StockChangedPayload(
    Guid TenantId,
    string Sku,
    int AvailableToSell,
    DateTime OccurredAt,
    string CorrelationId
);

/// <summary>
/// Sprint-7 plan U6 — hub event for one <c>FulfillmentSaga</c> state
/// transition. See <see cref="StockChangedPayload"/> for the envelope-mirror
/// rationale.
/// </summary>
public sealed record SagaTransitionedPayload(
    Guid TenantId,
    Guid OrderId,
    string FromState,
    string ToState,
    DateTime OccurredAt,
    string EventType,
    string CorrelationId
);
