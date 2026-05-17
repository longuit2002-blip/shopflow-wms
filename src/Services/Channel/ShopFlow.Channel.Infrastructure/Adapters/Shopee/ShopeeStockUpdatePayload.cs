using System.Text.Json;
using System.Text.Json.Serialization;
using ShopFlow.Channel.Application.Adapters;

namespace ShopFlow.Channel.Infrastructure.Adapters.Shopee;

/// <summary>
/// Sprint-5 U6 — wire shape for Shopee Open Platform v2's
/// <c>POST /api/v2/product/update_stock</c> endpoint. Snake-cased per the
/// real Shopee API contract.
/// </summary>
/// <remarks>
/// <para>For portfolio scope: <c>item_id</c> is parsed from
/// <see cref="StockUpdateRequest.ExternalSku"/> when numeric, else 0
/// (Phase-3 will route through a proper SKU → item lookup). <c>stock_list</c>
/// always carries a single no-variant entry (<c>model_id = 0</c>); the
/// sync engine flattens variant fan-out upstream.</para>
/// </remarks>
public sealed record ShopeeStockUpdatePayload(
    [property: JsonPropertyName("item_id")] long ItemId,
    [property: JsonPropertyName("stock_list")] IReadOnlyList<ShopeeStockListEntry> StockList
)
{
    public static ShopeeStockUpdatePayload From(StockUpdateRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var itemId = long.TryParse(request.ExternalSku, out var parsed) ? parsed : 0L;
        return new ShopeeStockUpdatePayload(
            ItemId: itemId,
            StockList: new[]
            {
                new ShopeeStockListEntry(ModelId: 0L, NormalStock: request.Quantity),
            }
        );
    }
}

public sealed record ShopeeStockListEntry(
    [property: JsonPropertyName("model_id")] long ModelId,
    [property: JsonPropertyName("normal_stock")] int NormalStock
);

/// <summary>
/// Shopee wire-shape JSON options. Separate from
/// <c>OutboxJsonOptions.Default</c> because Shopee's API is snake_case
/// while the outbox is camelCase; mixing would surface as silent data
/// corruption (Sprint-2.5 learning).
/// </summary>
internal static class ShopeeJson
{
    public static JsonSerializerOptions Options { get; } = new()
    {
        WriteIndented = false,
        PropertyNameCaseInsensitive = true,
    };
}
