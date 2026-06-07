using ShopFlow.SharedKernel.Domain;

namespace ShopFlow.Channel.Domain.Webhooks;

/// <summary>
/// One marketplace webhook persisted in the tenant DB per Tech Design v3.0
/// §6 + Sprint-4 plan U1/U3. The <c>(channel_id, provider_event_id)</c>
/// UNIQUE constraint on the underlying <c>webhook_events</c> table is the
/// idempotency primitive — first INSERT wins, replays surface as 23505 in
/// the repository and resolve to the existing row.
/// </summary>
/// <remarks>
/// <para>The aggregate carries the raw signed payload (JSONB on the wire)
/// for replay/audit and the <see cref="SignatureVerified"/> flag set true
/// only after HMAC verification in the receiver (Sprint-4 plan U3). Rows
/// with <c>SignatureVerified = false</c> should not exist in production —
/// the receiver returns 401 before insert — but the column documents the
/// invariant for downstream readers.</para>
/// </remarks>
public sealed class WebhookEvent : BaseEntity
{
    public Guid ChannelId { get; private set; }

    public ProviderEventId ProviderEventId { get; private set; } = null!;

    public string Payload { get; private set; } = string.Empty;

    public bool SignatureVerified { get; private set; }

    public WebhookProcessingStatus Status { get; private set; } = WebhookProcessingStatus.Received;

    public DateTime? ProcessedAt { get; private set; }

    public string? FailureReason { get; private set; }

    private WebhookEvent() { }

    public static Result<WebhookEvent> Create(
        Guid channelId,
        ProviderEventId providerEventId,
        string payload,
        bool signatureVerified
    )
    {
        ArgumentNullException.ThrowIfNull(providerEventId);

        if (channelId == Guid.Empty)
        {
            return Result<WebhookEvent>.Failure(
                "channel_id is required.",
                "webhook.channel_id_required"
            );
        }
        if (payload is null)
        {
            return Result<WebhookEvent>.Failure("payload is required.", "webhook.payload_required");
        }

        return Result<WebhookEvent>.Success(
            new WebhookEvent
            {
                ChannelId = channelId,
                ProviderEventId = providerEventId,
                Payload = payload,
                SignatureVerified = signatureVerified,
                Status = WebhookProcessingStatus.Received,
            }
        );
    }

    /// <summary>
    /// Received → Processed. Records when the downstream consumer (Outbound
    /// <c>OrderImportedConsumer</c> in Sprint-4) acked the work.
    /// </summary>
    public Result MarkProcessed(DateTime now)
    {
        if (Status == WebhookProcessingStatus.Processed)
        {
            return Result.Success();
        }
        if (Status != WebhookProcessingStatus.Received)
        {
            return Result.Failure(
                $"cannot mark processed from {Status}; only Received is eligible.",
                "webhook.invalid_state"
            );
        }
        Status = WebhookProcessingStatus.Processed;
        ProcessedAt = now;
        UpdatedAt = now;
        return Result.Success();
    }

    /// <summary>
    /// Received → Failed. Records an unrecoverable processing error
    /// (e.g., unmappable SKU). Surfaced to the operator queue in Phase-3
    /// Sprint-7.
    /// </summary>
    public Result MarkFailed(string reason, DateTime now)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            return Result.Failure("failure_reason is required.", "webhook.failure_reason_required");
        }
        if (Status != WebhookProcessingStatus.Received)
        {
            return Result.Failure(
                $"cannot mark failed from {Status}; only Received is eligible.",
                "webhook.invalid_state"
            );
        }
        Status = WebhookProcessingStatus.Failed;
        FailureReason = reason.Trim();
        ProcessedAt = now;
        UpdatedAt = now;
        return Result.Success();
    }
}
