using ShopFlow.SharedKernel.Domain;

namespace ShopFlow.Inventory.Domain.Events;

/// <summary>
/// Raised when <see cref="StockItem.AdjustStock"/> applies a manual or
/// system-driven adjustment (receiving, stock-take, damage write-off, …).
/// Carries the delta and reason for downstream audit projections.
/// </summary>
public sealed record StockAdjustedEvent(
    Guid TenantId,
    string Sku,
    int Delta,
    int NewTotalQuantity,
    StockAdjustmentReason Reason,
    Guid UserId,
    DateTime OccurredAt
) : IDomainEvent;
