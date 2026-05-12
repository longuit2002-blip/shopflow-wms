using ShopFlow.SharedKernel.Domain;

namespace ShopFlow.Inventory.Domain.Events;

/// <summary>
/// Raised when a reservation row transitions Pending → Confirmed. The
/// outbox interceptor (<c>ShopFlow.SharedKernel.Infrastructure.OutboxInterceptor</c>)
/// persists this in the same transaction; the multiplexed dispatcher
/// publishes it to RabbitMQ for downstream consumers (Outbound, Analytics).
/// </summary>
public sealed record StockReservedEvent(
    Guid ReservationId,
    string Sku,
    string OrderId,
    int Quantity,
    DateTime OccurredAt
) : IDomainEvent;
