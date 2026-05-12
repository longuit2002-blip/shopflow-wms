using ShopFlow.SharedKernel.Domain;

namespace ShopFlow.Inventory.Domain.Events;

/// <summary>
/// Raised when a reservation row transitions Pending → Released (explicit
/// cancellation) or Pending → Expired (TTL elapsed). Subscribers
/// distinguish the two via <see cref="Reason"/>.
/// </summary>
public sealed record StockReleasedEvent(
    Guid ReservationId,
    string Sku,
    string OrderId,
    int Quantity,
    StockReleaseReason Reason,
    DateTime OccurredAt
) : IDomainEvent;

public enum StockReleaseReason
{
    Cancelled = 0,
    Expired = 1,
}
