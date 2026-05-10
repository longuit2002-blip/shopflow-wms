using ShopFlow.SharedKernel.Domain;

namespace ShopFlow.Inventory.Domain.Events;

/// <summary>
/// Raised when a reservation row is appended successfully via the
/// conditional INSERT in Tech Design §7.2. Downstream consumers update
/// projection tables and channel sync queues with the new available qty.
/// </summary>
public sealed record StockReservedEvent(
    Guid TenantId,
    string Sku,
    int Quantity,
    Guid ReservationId,
    Guid OrderId,
    DateTime OccurredAt
) : IDomainEvent;
