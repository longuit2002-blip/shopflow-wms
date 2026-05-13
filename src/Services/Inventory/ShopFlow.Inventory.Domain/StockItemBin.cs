namespace ShopFlow.Inventory.Domain;

/// <summary>
/// Per-bin breakdown of a SKU's stock. Composite PK is <c>(sku, bin_id)</c>
/// per Sprint-2-redux plan R13. The invariant is:
/// <c>SUM(stock_item_bins.quantity WHERE sku=X) == stock_items.available +
/// stock_items.reserved</c> for any X with at least one bin row (plan R14).
/// </summary>
public sealed class StockItemBin
{
    public string Sku { get; private set; } = string.Empty;

    public long BinId { get; private set; }

    public int Quantity { get; private set; }

    private StockItemBin() { }

    internal static StockItemBin Create(string sku, long binId, int quantity)
    {
        if (string.IsNullOrWhiteSpace(sku))
        {
            throw new ArgumentException("sku is required.", nameof(sku));
        }
        if (quantity < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(quantity),
                quantity,
                "quantity must be >= 0."
            );
        }
        return new StockItemBin
        {
            Sku = sku.Trim(),
            BinId = binId,
            Quantity = quantity,
        };
    }

    internal void AdjustQuantity(int delta)
    {
        var next = Quantity + delta;
        if (next < 0)
        {
            throw new InvalidOperationException(
                $"stock_item_bins.quantity underflow for sku={Sku}, bin={BinId}: {Quantity} + {delta} < 0."
            );
        }
        Quantity = next;
    }
}
