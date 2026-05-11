namespace ShopFlow.SharedKernel.Infrastructure;

/// <summary>
/// Outbox row shape mirroring Tech Design v3.0 §5. The row is inserted in
/// the same transaction as the business write by
/// <see cref="OutboxInterceptor"/> and consumed by the multiplexed
/// <see cref="MultiplexedOutboxDispatcher{TContext}"/> via Mode A polling
/// (Tech Design v3.0 §5.3). LISTEN/NOTIFY (Mode B) and Debezium CDC
/// (Mode C) are follow-up plans.
/// </summary>
/// <remarks>
/// Per ADR-0003 the outbox table is per-tenant (one <c>outbox_messages</c>
/// table per tenant DB). The <see cref="TenantId"/> column is technically
/// redundant — the DB identifies the tenant — but is retained so the
/// dispatcher can construct envelope headers without an extra catalog
/// round-trip and so cross-tenant routing assertions in tests have a
/// clear signal.
/// </remarks>
public sealed class OutboxMessage
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>
    /// Redundant copy of the tenant id (the DB identity is the canonical
    /// boundary). Populated by the interceptor from the ambient
    /// <c>IRequestContext.TenantId</c> for diagnostic / envelope-stamping
    /// convenience.
    /// </summary>
    public Guid TenantId { get; set; }

    public string EventType { get; set; } = string.Empty;

    public string Payload { get; set; } = string.Empty;

    public string? TraceId { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime? ProcessedAt { get; set; }

    public int RetryCount { get; set; }

    public string? LastError { get; set; }
}
