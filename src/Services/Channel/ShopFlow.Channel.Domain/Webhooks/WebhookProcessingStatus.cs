namespace ShopFlow.Channel.Domain.Webhooks;

/// <summary>
/// Lifecycle of one persisted <see cref="WebhookEvent"/> per Sprint-4 plan
/// U1. <c>Received</c> = idempotent INSERT succeeded; <c>Processed</c> =
/// downstream consumer acked (Outbound order created or routed); <c>Failed</c>
/// = unrecoverable processing error (unmappable SKU, malformed payload after
/// signature verify, …). One-way: <c>Received</c> → <c>Processed</c> XOR
/// <c>Failed</c>. Replays of a row in any state are 200 no-ops by the
/// UNIQUE constraint, not state-machine logic.
/// </summary>
public enum WebhookProcessingStatus
{
    Received = 0,
    Processed = 1,
    Failed = 2,
}
