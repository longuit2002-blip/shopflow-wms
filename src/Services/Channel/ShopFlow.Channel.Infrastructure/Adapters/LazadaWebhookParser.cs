using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using ShopFlow.Channel.Application.Webhooks;
using ShopFlow.SharedKernel.Domain;
using ShopFlow.SharedKernel.Infrastructure;

namespace ShopFlow.Channel.Infrastructure.Adapters;

/// <summary>
/// Lazada-shaped webhook envelope parser (finish-line U7). Mirrors
/// <see cref="ShopeeWebhookParser"/>. The Lazada mock server emits an
/// envelope shaped like:
/// <code>
/// {
///   "event_id": "evt-1234-abcd",
///   "event_type": "order.created",
///   "data": { … marketplace-specific … }
/// }
/// </code>
/// The wrapper is our mock's envelope shape — not Lazada's real public API.
/// The parser normalises this into <see cref="WebhookEnvelope"/> without
/// inspecting <c>data</c> beyond passing the raw bytes through; the
/// orchestrator wires the data → <c>OrderImportedV1</c> mapping.
/// </summary>
/// <remarks>
/// Lazada order shape differs from Shopee: <c>data.order_id</c> (vs
/// <c>ordersn</c>), <c>data.order_items[]</c> with <c>sku</c> +
/// <c>quantity</c> (vs <c>items[]</c> with <c>item_sku</c> +
/// <c>model_quantity_purchased</c>), <c>data.delivery_carrier</c> (vs
/// <c>package_list[0].shipping_carrier</c>). Default shipping profile is
/// <c>"standard"</c> when the carrier is missing.
/// </remarks>
public sealed class LazadaWebhookParser
{
    public Result<WebhookEnvelope> Parse(Guid channelId, ReadOnlySpan<byte> body)
    {
        if (body.IsEmpty)
        {
            return Result<WebhookEnvelope>.Failure("webhook body is empty.", "lazada.body_empty");
        }

        try
        {
            var raw = Encoding.UTF8.GetString(body);
            var lazada = JsonSerializer.Deserialize<LazadaWebhookPayload>(
                body,
                OutboxJsonOptions.Default
            );
            if (lazada is null)
            {
                return Result<WebhookEnvelope>.Failure(
                    "lazada payload deserialised to null.",
                    "lazada.payload_null"
                );
            }
            if (string.IsNullOrWhiteSpace(lazada.EventId))
            {
                return Result<WebhookEnvelope>.Failure(
                    "lazada event_id missing.",
                    "lazada.event_id_required"
                );
            }
            if (string.IsNullOrWhiteSpace(lazada.EventType))
            {
                return Result<WebhookEnvelope>.Failure(
                    "lazada event_type missing.",
                    "lazada.event_type_required"
                );
            }

            // Lazada's envelope carries no top-level timestamp in the mock
            // shape; receiver-side processing time is stamped separately by
            // BaseEntity.CreatedAt, so OccurredAt = receipt time is fine.
            var occurredAt = DateTime.UtcNow;
            return Result<WebhookEnvelope>.Success(
                new WebhookEnvelope(
                    ChannelId: channelId,
                    ProviderEventId: lazada.EventId.Trim(),
                    EventType: lazada.EventType.Trim(),
                    RawPayload: raw,
                    OccurredAt: occurredAt
                )
            );
        }
        catch (JsonException ex)
        {
            return Result<WebhookEnvelope>.Failure(
                $"lazada body is malformed JSON: {ex.Message}",
                "lazada.body_malformed"
            );
        }
    }

    /// <summary>
    /// Finish-line U7 — extract the order-shape data from a Lazada
    /// <c>order.created</c> webhook's raw payload:
    /// <c>data.order_id</c>, <c>data.order_items[].sku</c>,
    /// <c>data.order_items[].quantity</c>, <c>data.delivery_carrier</c>.
    /// </summary>
    /// <remarks>
    /// Caller (<see cref="LazadaAdapter.ParseOrderCreated"/>) is
    /// responsible for event-type gating. This method assumes the
    /// payload is an <c>order.created</c> envelope and surfaces shape
    /// failures as <see cref="Result{T}.Failure"/> with stable codes.
    /// </remarks>
    public Result<ExternalOrderDraft> ParseOrderData(string rawPayload)
    {
        if (string.IsNullOrWhiteSpace(rawPayload))
        {
            return Result<ExternalOrderDraft>.Failure(
                "lazada.order: raw payload is empty.",
                "lazada.order.payload_empty"
            );
        }

        LazadaWebhookPayload? envelope;
        try
        {
            envelope = JsonSerializer.Deserialize<LazadaWebhookPayload>(
                rawPayload,
                OutboxJsonOptions.Default
            );
        }
        catch (JsonException ex)
        {
            return Result<ExternalOrderDraft>.Failure(
                $"lazada.order: raw payload is malformed JSON: {ex.Message}",
                "lazada.order.data_malformed"
            );
        }

        if (envelope is null || envelope.Data.ValueKind != JsonValueKind.Object)
        {
            return Result<ExternalOrderDraft>.Failure(
                "lazada.order: data object missing.",
                "lazada.order.data_missing"
            );
        }

        var data = envelope.Data;

        if (
            !data.TryGetProperty("order_id", out var orderIdElement)
            || orderIdElement.ValueKind != JsonValueKind.String
            || string.IsNullOrWhiteSpace(orderIdElement.GetString())
        )
        {
            return Result<ExternalOrderDraft>.Failure(
                "lazada.order: order_id is required.",
                "lazada.order.order_id_required"
            );
        }

        if (
            !data.TryGetProperty("order_items", out var itemsElement)
            || itemsElement.ValueKind != JsonValueKind.Array
            || itemsElement.GetArrayLength() == 0
        )
        {
            return Result<ExternalOrderDraft>.Failure(
                "lazada.order: order_items array is missing or empty.",
                "lazada.order.items_empty"
            );
        }

        var lines = new List<ExternalOrderLine>(itemsElement.GetArrayLength());
        var lineIndex = 0;
        foreach (var item in itemsElement.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object)
            {
                return Result<ExternalOrderDraft>.Failure(
                    $"lazada.order: order_items[{lineIndex}] is not an object.",
                    "lazada.order.line_malformed"
                );
            }

            if (
                !item.TryGetProperty("sku", out var skuElement)
                || skuElement.ValueKind != JsonValueKind.String
                || string.IsNullOrWhiteSpace(skuElement.GetString())
            )
            {
                return Result<ExternalOrderDraft>.Failure(
                    $"lazada.order: order_items[{lineIndex}].sku is required.",
                    "lazada.order.line_sku_required"
                );
            }

            if (
                !item.TryGetProperty("quantity", out var qtyElement)
                || qtyElement.ValueKind != JsonValueKind.Number
                || !qtyElement.TryGetInt32(out var qty)
                || qty <= 0
            )
            {
                return Result<ExternalOrderDraft>.Failure(
                    $"lazada.order: order_items[{lineIndex}].quantity must be a positive integer.",
                    "lazada.order.line_quantity_invalid"
                );
            }

            lines.Add(new ExternalOrderLine(skuElement.GetString()!.Trim(), qty));
            lineIndex++;
        }

        var shippingProfile = ExtractShippingProfile(data);

        return Result<ExternalOrderDraft>.Success(
            new ExternalOrderDraft(
                ChannelExternalOrderId: orderIdElement.GetString()!.Trim(),
                ShippingProfile: shippingProfile,
                Lines: lines
            )
        );
    }

    /// <summary>
    /// Map Lazada <c>data.delivery_carrier</c> to the internal
    /// <c>ShippingProfile</c> string. Ships the carrier name verbatim;
    /// operator-side profile catalog lookup is later work. Default
    /// <c>"standard"</c> when missing — keeps the downstream
    /// <c>OrderImportedV1.ShippingProfile</c> non-null.
    /// </summary>
    private static string ExtractShippingProfile(JsonElement data)
    {
        if (
            !data.TryGetProperty("delivery_carrier", out var carrier)
            || carrier.ValueKind != JsonValueKind.String
            || string.IsNullOrWhiteSpace(carrier.GetString())
        )
        {
            return "standard";
        }

        return carrier.GetString()!.Trim();
    }

    /// <summary>
    /// Lazada webhook payload shape. Forward-compatible — unknown fields
    /// in <c>data</c> are preserved as JsonElement and pass through to
    /// downstream handlers untouched.
    /// </summary>
    public sealed class LazadaWebhookPayload
    {
        [JsonPropertyName("event_id")]
        public string? EventId { get; set; }

        [JsonPropertyName("event_type")]
        public string? EventType { get; set; }

        [JsonPropertyName("data")]
        public JsonElement Data { get; set; }
    }
}
