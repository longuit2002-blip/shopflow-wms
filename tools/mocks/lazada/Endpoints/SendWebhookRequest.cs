namespace ShopFlow.Mocks.Lazada.Endpoints;

/// <summary>
/// Test-driver payload for <c>POST /__send-webhook</c> (finish-line U7).
/// The caller specifies which channel + event to emit; the mock signs the
/// resulting Lazada-shape envelope with the configured secret and POSTs it
/// to the Channel.Api receiver URL with the <c>X-Lazada-Signature</c>
/// header. Mirrors the Shopee mock's <c>SendWebhookRequest</c> but carries
/// optional line items so the receiver can parse a real order.created body.
/// </summary>
public sealed record SendWebhookRequest(
    Guid ChannelId,
    string? EventId,
    string ChannelType,
    string EventType,
    string ExternalOrderId,
    LazadaItem[]? Items,
    string? DeliveryCarrier,
    string? ReceiverBaseUrl
);

/// <summary>
/// Lazada order line for the mock's <c>order.created</c> body. Maps to the
/// <c>data.order_items[]</c> shape the <c>LazadaWebhookParser</c> reads:
/// <c>sku</c> + <c>quantity</c>.
/// </summary>
public sealed record LazadaItem(string Sku, int Quantity);
