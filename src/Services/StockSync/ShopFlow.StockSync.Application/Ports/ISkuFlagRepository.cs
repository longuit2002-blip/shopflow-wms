namespace ShopFlow.StockSync.Application.Ports;

/// <summary>
/// Application-layer port the StockSync consumer + admin endpoint consume
/// to read/write the <c>sku_flag</c> table (Sprint-5 plan U7).
/// Implementation lands in U7's <c>SkuFlagRepository</c> against the
/// tenant-scoped <c>StockSyncDbContext</c>.
/// </summary>
public interface ISkuFlagRepository
{
    /// <summary>
    /// Returns <c>true</c> when an explicit flash-sale flag exists for
    /// <paramref name="sku"/> and is set; <c>false</c> when the row is
    /// absent or the flag is cleared. The consumer stamps this on the
    /// <see cref="ShopFlow.StockSync.Application.Coalescing.CoalesceEntry.IsFlashSale"/>
    /// at upsert time so the flush path doesn't need a second DB hit.
    /// </summary>
    Task<bool> IsFlashSaleAsync(string sku, CancellationToken ct);

    /// <summary>
    /// Idempotent upsert called by the admin
    /// <c>PUT /api/skus/{sku}/flag</c> endpoint (Sprint-5 U7).
    /// </summary>
    Task SetFlashSaleAsync(string sku, bool isFlashSale, CancellationToken ct);
}
