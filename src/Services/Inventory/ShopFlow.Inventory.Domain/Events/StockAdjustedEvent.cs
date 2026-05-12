using ShopFlow.SharedKernel.Domain;

namespace ShopFlow.Inventory.Domain.Events;

/// <summary>
/// Raised when stock-on-hand changes outside the reservation flow — inbound
/// receipt, cycle-count correction, damage write-off, return restock. The
/// <see cref="StockAdjustmentReason"/> is the audit anchor; downstream
/// services (Analytics, ChannelSync) decide whether to act per-reason.
/// </summary>
public sealed record StockAdjustedEvent(
    string Sku,
    int Delta,
    StockAdjustmentReason Reason,
    DateTime OccurredAt
) : IDomainEvent;
