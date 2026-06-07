using ShopFlow.SharedKernel.Domain;

namespace ShopFlow.Inventory.Domain.Events;

/// <summary>
/// Catch-all event for the stock-sync engine (Tech Design v3.0 §5) — fired
/// any time the published <c>(Available, Reserved)</c> count for a SKU
/// changes, regardless of the underlying cause (reservation, confirm,
/// release, adjustment). The sync engine coalesces these into per-channel
/// pushes within a token-bucket budget. Subscribers should treat this as
/// a hint to re-read; the canonical numbers live on the
/// <c>StockItem</c> aggregate.
/// </summary>
public sealed record StockChangedEvent(string Sku, int Available, int Reserved, DateTime OccurredAt)
    : IDomainEvent;
