namespace ShopFlow.SharedKernel.Domain;

/// <summary>
/// Marker for domain events raised on aggregate roots and collected by the
/// outbox interceptor. Per AGENTS.md §6.39 every event carries
/// <c>tenant_id</c> and an <c>occurred_at</c> UTC timestamp on its envelope;
/// the correlation id is attached by the outbox dispatcher from the
/// ambient <see cref="Application.IRequestContext"/>.
/// </summary>
public interface IDomainEvent
{
    Guid TenantId { get; }
    DateTime OccurredAt { get; }
}
