namespace ShopFlow.Channel.Application.Webhooks;

/// <summary>
/// Normalised cross-marketplace webhook payload per Sprint-4 plan U3.
/// The per-channel <see cref="Adapters.IChannelAdapter"/> (U5) parses raw
/// provider bytes into this shape; the orchestrator
/// (<see cref="IngestWebhookService"/>) treats it as opaque so the receiver
/// flow stays adapter-agnostic.
/// </summary>
/// <param name="ChannelId">
/// Resolved channel id from URL routing or signed payload extraction.
/// </param>
/// <param name="ProviderEventId">
/// Marketplace-supplied event id — the second half of the UNIQUE
/// idempotency key.
/// </param>
/// <param name="EventType">
/// Adapter-normalised event-type identifier (e.g. <c>"order.created"</c>).
/// Used in U8 to decide which cross-module command/event to emit.
/// </param>
/// <param name="RawPayload">
/// JSON payload — stored verbatim in <c>webhook_events.payload</c> so a
/// future operator surface can replay or audit.
/// </param>
/// <param name="OccurredAt">
/// Marketplace-side timestamp when the event was emitted. Receiver-side
/// processing time is stamped separately by <c>BaseEntity.CreatedAt</c>.
/// </param>
public sealed record WebhookEnvelope(
    Guid ChannelId,
    string ProviderEventId,
    string EventType,
    string RawPayload,
    DateTime OccurredAt
);
