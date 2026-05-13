namespace ShopFlow.Outbound.Application.Ports;

/// <summary>
/// Application-layer write port over the Outbound module's per-tenant
/// <c>outbound_outbox_messages</c> table (Sprint-2.5 per-module prefix).
/// Lets the orchestration code enqueue a typed integration event for
/// the multiplexed dispatcher to publish. The event payload is
/// serialized at enqueue time with
/// <c>ShopFlow.SharedKernel.Infrastructure.OutboxJsonOptions.Default</c>;
/// the dispatcher reads <c>EventType</c> via <c>Type.GetType</c> at
/// dispatch time and publishes through MassTransit (plan R10).
/// </summary>
/// <remarks>
/// Mirrors Sprint-2-redux's <c>ShopFlow.Inbound.Application.Ports.IInboundOutbox</c>.
/// The explicit-write port exists instead of the domain-event +
/// <c>OutboxInterceptor</c> harvest path because the cross-module
/// contracts (e.g. <c>OrderPlacedV1</c>, U3) live in
/// <c>ShopFlow.Contracts</c>; adding <c>IDomainEvent</c> to them would
/// create the same Application → Contracts cycle that Sprint-1-redux's
/// <c>ReservationRepository</c> avoided.
/// </remarks>
public interface IOutboundOutbox
{
    /// <summary>
    /// Enqueue an integration event for publish. The event flows on the
    /// caller's open EF transaction — committed atomically with the
    /// business write via <see cref="IUnitOfWork.SaveChangesAsync"/>.
    /// </summary>
    /// <param name="eventType">
    /// Wire-format event type (assembly-qualified type name resolved by
    /// the dispatcher via <c>Type.GetType</c>). For U2 this is a stub
    /// payload type; U3 swaps it for <c>ShopFlow.Contracts.Outbound.OrderPlacedV1</c>.
    /// </param>
    /// <param name="payload">
    /// Event payload object; serialized to JSON with
    /// <c>OutboxJsonOptions.Default</c>.
    /// </param>
    Task AppendAsync(string eventType, object payload, CancellationToken ct);
}
