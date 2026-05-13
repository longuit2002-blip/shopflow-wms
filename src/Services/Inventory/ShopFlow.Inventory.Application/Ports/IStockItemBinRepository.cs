using ShopFlow.Inventory.Domain;

namespace ShopFlow.Inventory.Application.Ports;

/// <summary>
/// Surface over the per-bin stock breakdown table per Sprint-2-redux
/// plan R13. <see cref="UpsertQuantityAsync"/> is the load-bearing
/// write — invoked by the bin-aware <c>StockItemRepository.AdjustAsync</c>
/// in U5.
/// </summary>
public interface IStockItemBinRepository
{
    Task<StockItemBin?> FindBySkuBinAsync(string sku, long binId, CancellationToken ct);

    /// <summary>
    /// INSERT new (sku, bin_id) row at <paramref name="quantity"/>, OR
    /// UPDATE existing row by adding <paramref name="delta"/> to its
    /// quantity. Returns the new running quantity.
    /// </summary>
    Task<int> UpsertQuantityAsync(string sku, long binId, int delta, CancellationToken ct);
}
