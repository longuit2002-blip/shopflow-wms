using ShopFlow.SharedKernel.Domain;

namespace ShopFlow.Inventory.Domain.Events;

/// <summary>
/// Raised when an active reservation transitions to Released or Expired.
/// Per Tech Design §7.4 the expiry worker emits this for each row it
/// flips to status='expired'.
/// </summary>
public sealed record StockReleasedEvent(
    Guid TenantId,
    string Sku,
    int Quantity,
    Guid ReservationId,
    Guid OrderId,
    ReservationStatus FinalStatus,
    DateTime OccurredAt
) : IDomainEvent;
