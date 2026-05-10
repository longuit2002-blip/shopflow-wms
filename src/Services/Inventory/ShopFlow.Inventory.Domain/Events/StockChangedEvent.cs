using ShopFlow.SharedKernel.Domain;

namespace ShopFlow.Inventory.Domain.Events;

/// <summary>
/// Raised when <see cref="StockItem.ConfirmDeduction"/> mutates the on-hand
/// quantity. Carried through the outbox by the kernel interceptor; channel
/// adapters consume it to push the new availability to marketplaces.
/// </summary>
public sealed record StockChangedEvent(
    Guid TenantId,
    string Sku,
    int NewTotalQuantity,
    int NewAvailableQuantity,
    DateTime OccurredAt
) : IDomainEvent;
