namespace ShopFlow.Contracts.Inventory;

/// <summary>
/// Inventory-emitted result event when a Pending → Confirmed transition
/// completes for all rows under <c>order_id</c>. Sprint-3-redux: emitted
/// in response to <see cref="ConfirmStockV1"/> from the saga's
/// AwaitingShip → Shipped path. Treated as a side-effect notification;
/// the saga does NOT correlate on this event (the saga is already at
/// Shipped before publishing the command per Sprint-2-redux's
/// "command publishes after state commits" pattern).
/// </summary>
public sealed record StockConfirmedV1(Guid OrderId, Guid TenantId, DateTime OccurredAt);
