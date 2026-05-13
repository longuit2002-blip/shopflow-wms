namespace ShopFlow.Contracts.Inventory;

/// <summary>
/// Inventory-emitted result event when a Pending → Released transition
/// completes. Sprint-3-redux: <see cref="OrderLineIds"/> carries the
/// actually-released line ids (NOT the requested set), supporting the
/// saga's <c>ReleasedLineSkus</c> Set-based dedup against MassTransit
/// at-least-once redelivery per the K11 supplementary note. Empty list ⇒
/// idempotent no-op release (everything already in terminal state).
/// </summary>
public sealed record StockReleasedV1(
    Guid OrderId,
    Guid TenantId,
    IReadOnlyList<string> OrderLineIds,
    DateTime OccurredAt
);
