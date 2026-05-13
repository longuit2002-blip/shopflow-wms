namespace ShopFlow.Channel.Application.Webhooks;

/// <summary>
/// Outcome of <see cref="IngestWebhookService.IngestAsync"/>.
/// <see cref="IsDuplicate"/> = true means the receiver saw a replay and did
/// NOT enqueue a downstream outbox row; receiver responds 200 either way.
/// </summary>
public sealed record IngestWebhookResult(Guid EventId, bool IsDuplicate);
