namespace ShopFlow.Contracts.Inventory;

/// <summary>
/// Saga-issued command from the Outbound fulfillment saga (U4) on the
/// AwaitingShip → Shipped transition. Confirms ALL Pending ledger rows
/// for <see cref="OrderId"/> per Sprint-3-redux U3: no per-line list
/// needed because confirm operates on the whole order (the saga only
/// reaches AwaitingShip when every line successfully reserved).
/// </summary>
public sealed record ConfirmStockV1(Guid OrderId, Guid TenantId);
