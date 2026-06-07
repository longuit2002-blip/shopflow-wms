namespace ShopFlow.StockSync.Domain.Aggregates;

/// <summary>
/// Audit row recording one push attempt of <c>available_to_sell</c> to a
/// downstream marketplace adapter (Sprint-5 plan R12/U5).
/// </summary>
/// <remarks>
/// <para>Primary key is <c>BIGSERIAL</c>. The <c>IdempotencyKey</c> column
/// carries a UNIQUE constraint — the dispatcher's deterministic key
/// (<c>tenantId:sku:channel:observedAt</c>) catches MassTransit
/// at-least-once redelivery via 23505 (Sprint-1-redux pattern).</para>
/// <para><see cref="Status"/> is <c>"Success"</c>, <c>"Failed"</c>, or
/// <c>"BreakerOpen"</c>. <see cref="ErrorCode"/> is a stable string
/// produced by the adapter (e.g., <c>shopee.push.5xx</c>).</para>
/// </remarks>
public sealed class PushLogEntry
{
    public long Id { get; private set; }

    public Guid TenantId { get; private set; }

    public string ChannelType { get; private set; } = default!;

    public string Sku { get; private set; } = default!;

    public int Available { get; private set; }

    public string IdempotencyKey { get; private set; } = default!;

    public string Status { get; private set; } = default!;

    public string? ErrorCode { get; private set; }

    public int LatencyMs { get; private set; }

    public DateTime ObservedAt { get; private set; }

    public DateTime PushedAt { get; private set; }

    private PushLogEntry() { }

    public static PushLogEntry MarkSucceeded(
        Guid tenantId,
        string channelType,
        string sku,
        int available,
        string idempotencyKey,
        int latencyMs,
        DateTime observedAt,
        DateTime pushedAt
    )
    {
        ValidateInputs(tenantId, channelType, sku, idempotencyKey, latencyMs);

        return new PushLogEntry
        {
            TenantId = tenantId,
            ChannelType = channelType,
            Sku = sku,
            Available = available,
            IdempotencyKey = idempotencyKey,
            Status = "Success",
            ErrorCode = null,
            LatencyMs = latencyMs,
            ObservedAt = observedAt,
            PushedAt = pushedAt,
        };
    }

    public static PushLogEntry MarkFailed(
        Guid tenantId,
        string channelType,
        string sku,
        int available,
        string idempotencyKey,
        string errorCode,
        int latencyMs,
        DateTime observedAt,
        DateTime pushedAt
    )
    {
        ValidateInputs(tenantId, channelType, sku, idempotencyKey, latencyMs);

        if (string.IsNullOrWhiteSpace(errorCode))
        {
            throw new ArgumentException("Error code must be non-empty", nameof(errorCode));
        }

        return new PushLogEntry
        {
            TenantId = tenantId,
            ChannelType = channelType,
            Sku = sku,
            Available = available,
            IdempotencyKey = idempotencyKey,
            Status = "Failed",
            ErrorCode = errorCode,
            LatencyMs = latencyMs,
            ObservedAt = observedAt,
            PushedAt = pushedAt,
        };
    }

    public static PushLogEntry MarkBreakerOpen(
        Guid tenantId,
        string channelType,
        string sku,
        int available,
        string idempotencyKey,
        DateTime observedAt,
        DateTime rejectedAt
    )
    {
        ValidateInputs(tenantId, channelType, sku, idempotencyKey, latencyMs: 0);

        return new PushLogEntry
        {
            TenantId = tenantId,
            ChannelType = channelType,
            Sku = sku,
            Available = available,
            IdempotencyKey = idempotencyKey,
            Status = "BreakerOpen",
            ErrorCode = "stocksync.breaker.open",
            LatencyMs = 0,
            ObservedAt = observedAt,
            PushedAt = rejectedAt,
        };
    }

    private static void ValidateInputs(
        Guid tenantId,
        string channelType,
        string sku,
        string idempotencyKey,
        int latencyMs
    )
    {
        if (tenantId == Guid.Empty)
        {
            throw new ArgumentException("TenantId must be non-empty", nameof(tenantId));
        }

        if (string.IsNullOrWhiteSpace(channelType))
        {
            throw new ArgumentException("ChannelType must be non-empty", nameof(channelType));
        }

        if (string.IsNullOrWhiteSpace(sku))
        {
            throw new ArgumentException("Sku must be non-empty", nameof(sku));
        }

        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            throw new ArgumentException("IdempotencyKey must be non-empty", nameof(idempotencyKey));
        }

        if (latencyMs < 0)
        {
            throw new ArgumentException("LatencyMs must be non-negative", nameof(latencyMs));
        }
    }
}
