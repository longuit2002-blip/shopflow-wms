namespace ShopFlow.SharedKernel.Domain;

/// <summary>
/// Base class for entities with an identity and a domain-event buffer.
/// Mirrors Tech Design v3.0 §20 (Shared Kernel). The buffer is drained by
/// the outbox interceptor in <see cref="Infrastructure.OutboxInterceptor"/>;
/// callers must not consume <see cref="DomainEvents"/> for any other purpose.
/// </summary>
/// <remarks>
/// Per ADR-0003 (DB-per-tenant) the v2.0 <c>TenantId</c> field is removed —
/// the database identity is the tenant boundary, so no business entity
/// carries <c>tenant_id</c>. Tenant context flows through
/// <see cref="Application.IRequestContext"/> and the per-request DbContext
/// factory; the outbox interceptor reads tenant scope from there at write
/// time. AGENTS.md §3.14 enforces "no tenant_id on business tables".
/// </remarks>
public abstract class BaseEntity
{
    public Guid Id { get; protected set; } = Guid.NewGuid();

    public DateTime CreatedAt { get; protected set; } = DateTime.UtcNow;

    public DateTime? UpdatedAt { get; protected set; }

    private readonly List<IDomainEvent> _domainEvents = new();

    public IReadOnlyList<IDomainEvent> DomainEvents => _domainEvents.AsReadOnly();

    protected void RaiseDomainEvent(IDomainEvent e) => _domainEvents.Add(e);

    public void ClearDomainEvents() => _domainEvents.Clear();
}
