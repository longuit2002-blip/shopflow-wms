using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using ShopFlow.Channel.Application.Webhooks;
using ShopFlow.SharedKernel.Domain;
using ShopFlow.SharedKernel.Infrastructure;

namespace ShopFlow.Channel.Infrastructure.Adapters;

/// <summary>
/// Shopee-shaped webhook envelope parser per Sprint-4 plan U5. The
/// Shopee mock server (U7) emits an envelope shaped like:
/// <code>
/// {
///   "event_id": "evt-1234-abcd",
///   "event_type": "order.created",
///   "shop_id": 12345,
///   "timestamp": 1730000000,
///   "data": { … marketplace-specific … }
/// }
/// </code>
/// The parser normalises this into <see cref="WebhookEnvelope"/> without
/// inspecting <c>data</c> beyond passing the raw bytes through — Sprint-4
/// U8 wires the data → <c>OrderImportedV1</c> mapping; for U5 the envelope
/// is the unit of work.
/// </summary>
public sealed class ShopeeWebhookParser
{
    public Result<WebhookEnvelope> Parse(Guid channelId, ReadOnlySpan<byte> body)
    {
        if (body.IsEmpty)
        {
            return Result<WebhookEnvelope>.Failure(
                "webhook body is empty.",
                "shopee.body_empty"
            );
        }

        try
        {
            var raw = Encoding.UTF8.GetString(body);
            var shopee = JsonSerializer.Deserialize<ShopeeWebhookPayload>(
                body,
                OutboxJsonOptions.Default
            );
            if (shopee is null)
            {
                return Result<WebhookEnvelope>.Failure(
                    "shopee payload deserialised to null.",
                    "shopee.payload_null"
                );
            }
            if (string.IsNullOrWhiteSpace(shopee.EventId))
            {
                return Result<WebhookEnvelope>.Failure(
                    "shopee event_id missing.",
                    "shopee.event_id_required"
                );
            }
            if (string.IsNullOrWhiteSpace(shopee.EventType))
            {
                return Result<WebhookEnvelope>.Failure(
                    "shopee event_type missing.",
                    "shopee.event_type_required"
                );
            }

            var occurredAt = DateTimeOffset.FromUnixTimeSeconds(shopee.Timestamp).UtcDateTime;
            return Result<WebhookEnvelope>.Success(
                new WebhookEnvelope(
                    ChannelId: channelId,
                    ProviderEventId: shopee.EventId.Trim(),
                    EventType: shopee.EventType.Trim(),
                    RawPayload: raw,
                    OccurredAt: occurredAt
                )
            );
        }
        catch (JsonException ex)
        {
            return Result<WebhookEnvelope>.Failure(
                $"shopee body is malformed JSON: {ex.Message}",
                "shopee.body_malformed"
            );
        }
    }

    /// <summary>
    /// Sprint-4.5 U1 — extract the order-shape data from a Shopee
    /// <c>order.created</c> webhook's raw payload. Reads field names from
    /// the real Shopee Open Platform v2 wire format per
    /// <c>tests/fixtures/channels/shopee/webhook-order-created.json</c>:
    /// <c>data.ordersn</c>, <c>data.items[].item_sku</c>,
    /// <c>data.items[].model_quantity_purchased</c>,
    /// <c>data.package_list[0].shipping_carrier</c>.
    /// </summary>
    /// <remarks>
    /// Caller (<see cref="ShopeeAdapter.ParseOrderCreated"/>) is
    /// responsible for event-type gating. This method assumes the
    /// payload is an <c>order.created</c> envelope and surfaces shape
    /// failures as <see cref="Result{T}.Failure"/> with stable codes.
    /// </remarks>
    public Result<ExternalOrderDraft> ParseOrderData(string rawPayload)
    {
        if (string.IsNullOrWhiteSpace(rawPayload))
        {
            return Result<ExternalOrderDraft>.Failure(
                "shopee.order: raw payload is empty.",
                "shopee.order.payload_empty"
            );
        }

        ShopeeWebhookPayload? envelope;
        try
        {
            envelope = JsonSerializer.Deserialize<ShopeeWebhookPayload>(
                rawPayload,
                OutboxJsonOptions.Default
            );
        }
        catch (JsonException ex)
        {
            return Result<ExternalOrderDraft>.Failure(
                $"shopee.order: raw payload is malformed JSON: {ex.Message}",
                "shopee.order.data_malformed"
            );
        }

        if (envelope is null || envelope.Data.ValueKind != JsonValueKind.Object)
        {
            return Result<ExternalOrderDraft>.Failure(
                "shopee.order: data object missing.",
                "shopee.order.data_missing"
            );
        }

        var data = envelope.Data;

        if (
            !data.TryGetProperty("ordersn", out var ordersnElement)
            || ordersnElement.ValueKind != JsonValueKind.String
            || string.IsNullOrWhiteSpace(ordersnElement.GetString())
        )
        {
            return Result<ExternalOrderDraft>.Failure(
                "shopee.order: ordersn is required.",
                "shopee.order.ordersn_required"
            );
        }

        if (
            !data.TryGetProperty("items", out var itemsElement)
            || itemsElement.ValueKind != JsonValueKind.Array
            || itemsElement.GetArrayLength() == 0
        )
        {
            return Result<ExternalOrderDraft>.Failure(
                "shopee.order: items array is missing or empty.",
                "shopee.order.items_empty"
            );
        }

        var lines = new List<ExternalOrderLine>(itemsElement.GetArrayLength());
        var lineIndex = 0;
        foreach (var item in itemsElement.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object)
            {
                return Result<ExternalOrderDraft>.Failure(
                    $"shopee.order: items[{lineIndex}] is not an object.",
                    "shopee.order.line_malformed"
                );
            }

            if (
                !item.TryGetProperty("item_sku", out var skuElement)
                || skuElement.ValueKind != JsonValueKind.String
                || string.IsNullOrWhiteSpace(skuElement.GetString())
            )
            {
                return Result<ExternalOrderDraft>.Failure(
                    $"shopee.order: items[{lineIndex}].item_sku is required.",
                    "shopee.order.line_sku_required"
                );
            }

            if (
                !item.TryGetProperty("model_quantity_purchased", out var qtyElement)
                || qtyElement.ValueKind != JsonValueKind.Number
                || !qtyElement.TryGetInt32(out var qty)
                || qty <= 0
            )
            {
                return Result<ExternalOrderDraft>.Failure(
                    $"shopee.order: items[{lineIndex}].model_quantity_purchased must be a positive integer.",
                    "shopee.order.line_quantity_invalid"
                );
            }

            lines.Add(new ExternalOrderLine(skuElement.GetString()!.Trim(), qty));
            lineIndex++;
        }

        var shippingProfile = ExtractShippingProfile(data);

        return Result<ExternalOrderDraft>.Success(
            new ExternalOrderDraft(
                ChannelExternalOrderId: ordersnElement.GetString()!.Trim(),
                ShippingProfile: shippingProfile,
                Lines: lines
            )
        );
    }

    /// <summary>
    /// Map Shopee <c>data.package_list[0].shipping_carrier</c> to the
    /// internal <c>ShippingProfile</c> string. Sprint-4.5 ships the
    /// carrier name verbatim; operator-side profile catalog lookup is
    /// Sprint-6+ work. Default <c>"default"</c> when missing — keeps
    /// the downstream <c>OrderImportedV1.ShippingProfile</c> non-null.
    /// </summary>
    private static string ExtractShippingProfile(JsonElement data)
    {
        if (
            !data.TryGetProperty("package_list", out var packages)
            || packages.ValueKind != JsonValueKind.Array
            || packages.GetArrayLength() == 0
        )
        {
            return "default";
        }

        var first = packages[0];
        if (
            first.ValueKind != JsonValueKind.Object
            || !first.TryGetProperty("shipping_carrier", out var carrier)
            || carrier.ValueKind != JsonValueKind.String
            || string.IsNullOrWhiteSpace(carrier.GetString())
        )
        {
            return "default";
        }

        return carrier.GetString()!.Trim();
    }

    /// <summary>
    /// Shopee webhook payload shape. Forward-compatible — unknown fields
    /// in <c>data</c> are preserved as JsonElement and pass through to
    /// downstream handlers untouched.
    /// </summary>
    public sealed class ShopeeWebhookPayload
    {
        [JsonPropertyName("event_id")]
        public string? EventId { get; set; }

        [JsonPropertyName("event_type")]
        public string? EventType { get; set; }

        [JsonPropertyName("shop_id")]
        public long ShopId { get; set; }

        [JsonPropertyName("timestamp")]
        public long Timestamp { get; set; }

        [JsonPropertyName("data")]
        public JsonElement Data { get; set; }
    }
}
