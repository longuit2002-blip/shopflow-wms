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
