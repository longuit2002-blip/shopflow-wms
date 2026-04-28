namespace ShopFlow.SharedKernel.Infrastructure;

/// <summary>
/// Outbox row shape mirroring Tech Design §11.1. The row is inserted in the
/// same transaction as the business write by <see cref="OutboxInterceptor"/>
/// and consumed by <see cref="OutboxDispatcher"/> via Mode A polling
/// (Tech Design §11.3). LISTEN/NOTIFY (Mode B) is a follow-up plan.
/// </summary>
/// <remarks>
/// The Postgres table is <c>outbox_messages</c> partitioned monthly by
/// <c>created_at</c>; the partitioning is set up in U6's initial migration,
/// not here. This kernel type only defines the row shape so that
/// <c>DbContext.Set&lt;OutboxMessage&gt;()</c> in the interceptor compiles
/// against any module's DbContext that has registered the entity.
/// </remarks>
public sealed class OutboxMessage
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid TenantId { get; set; }

    public string EventType { get; set; } = string.Empty;

    public string Payload { get; set; } = string.Empty;

    public string? TraceId { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime? ProcessedAt { get; set; }

    public int RetryCount { get; set; }

    public string? LastError { get; set; }
}
