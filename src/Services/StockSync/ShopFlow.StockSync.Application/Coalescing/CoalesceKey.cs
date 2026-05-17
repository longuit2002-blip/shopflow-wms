namespace ShopFlow.StockSync.Application.Coalescing;

/// <summary>
/// Composite dictionary key for the in-memory coalescing buffer (Sprint-5
/// plan KTD4). Value equality on <c>(TenantId, Sku, ChannelType)</c> means
/// repeated updates to the same SKU in the same channel for the same tenant
/// collapse onto one bucket inside the coalesce window — the headline
/// optimisation behind R3 ("emit ≤ 1 push per SKU per channel per window").
/// </summary>
/// <param name="TenantId">Owning tenant (from <c>StockLevelChangedV1.TenantId</c>).</param>
/// <param name="Sku">Internal SKU string (case-sensitive, matches Inventory).</param>
/// <param name="ChannelType">Marketplace channel slug, e.g. <c>"shopee"</c> / <c>"lazada"</c>.</param>
public readonly record struct CoalesceKey(Guid TenantId, string Sku, string ChannelType);
