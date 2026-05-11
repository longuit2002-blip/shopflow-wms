namespace ShopFlow.SharedKernel.Domain;

/// <summary>
/// Marker for domain events raised on aggregate roots and collected by the
/// outbox interceptor. Per AGENTS.md §6.42 every published integration event
/// carries <c>tenant_id</c>, <c>correlation_id</c>, and <c>occurred_at</c>
/// UTC on the envelope.
/// </summary>
/// <remarks>
/// Per ADR-0003 (DB-per-tenant) the v2.0 <c>TenantId</c> property is removed
/// from the domain event itself — the database identity is the tenant
/// boundary. The outbox dispatcher stamps <c>tenant_id</c> on the message
/// envelope from the tenant DB it iterates, and <c>correlation_id</c> from
/// the row's trace id captured at write time. The domain event only carries
/// the business-meaningful timestamp here.
/// </remarks>
public interface IDomainEvent
{
    DateTime OccurredAt { get; }
}
