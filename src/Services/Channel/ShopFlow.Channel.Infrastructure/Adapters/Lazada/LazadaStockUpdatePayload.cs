using System.Text.Json;
using System.Text.Json.Serialization;
using ShopFlow.Channel.Application.Adapters;

namespace ShopFlow.Channel.Infrastructure.Adapters.Lazada;

/// <summary>
/// Finish-line U7 — wire shape for the Lazada
/// <c>POST /api/v3/product/update_stock</c> endpoint. Mirrors
/// <c>ShopeeStockUpdatePayload</c> but for the Lazada v3 contract shape:
/// a seller-SKU keyed stock list rather than Shopee's item-id + model fan-out.
/// </summary>
/// <remarks>
/// <para>For portfolio scope: <c>seller_sku</c> carries
/// <see cref="StockUpdateRequest.ExternalSku"/> verbatim (Lazada keys
/// stock by seller SKU, not numeric item id), and <c>stock</c> is the new
/// available quantity. The single-entry <c>sellable_stock</c> list keeps
/// the shape extensible if multi-warehouse fan-out lands later.</para>
/// </remarks>
public sealed record LazadaStockUpdatePayload(
    [property: JsonPropertyName("seller_sku")] string SellerSku,
    [property: JsonPropertyName("sellable_stock")] IReadOnlyList<LazadaStockEntry> SellableStock
)
{
    public static LazadaStockUpdatePayload From(StockUpdateRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return new LazadaStockUpdatePayload(
            SellerSku: request.ExternalSku,
            SellableStock: new[]
            {
                new LazadaStockEntry(WarehouseCode: "DEFAULT", Stock: request.Quantity),
            }
        );
    }
}

public sealed record LazadaStockEntry(
    [property: JsonPropertyName("warehouse_code")] string WarehouseCode,
    [property: JsonPropertyName("stock")] int Stock
);

/// <summary>
/// Lazada wire-shape JSON options. Separate from
/// <c>OutboxJsonOptions.Default</c> because the marketplace API is
/// snake_case while the outbox is camelCase; mixing would surface as silent
/// data corruption (Sprint-2.5 learning, mirrored from <c>ShopeeJson</c>).
/// </summary>
internal static class LazadaJson
{
    public static JsonSerializerOptions Options { get; } =
        new() { WriteIndented = false, PropertyNameCaseInsensitive = true };
}
