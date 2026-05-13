namespace ShopFlow.Inbound.Application.Ports;

/// <summary>
/// Application-layer write port over the Inbound module's per-tenant
/// <c>outbox_messages</c> table. Lets the orchestration service enqueue
/// a typed integration event for the multiplexed dispatcher to publish
/// (per Sprint-2-redux plan R10). The event payload is serialized at
/// enqueue time; the dispatcher reads <c>EventType</c> via
/// <c>Type.GetType</c> at dispatch time and publishes the typed message
/// through MassTransit.
/// </summary>
/// <remarks>
/// This port exists instead of relying on the domain-event +
/// OutboxInterceptor harvest path because the cross-module contract
/// (<c>ShopFlow.Contracts.Inbound.InboundConfirmedV1</c>) is not a
/// domain event — adding <c>IDomainEvent</c> to it would create a
/// dependency cycle since <c>ShopFlow.SharedKernel</c> already
/// references <c>ShopFlow.Contracts</c>. Sprint-1-redux's
/// <c>ReservationRepository</c> uses the same explicit-write pattern
/// for the same reason.
/// </remarks>
public interface IInboundOutbox
{
    void Enqueue<T>(T integrationEvent, DateTime occurredAt)
        where T : class;
}
