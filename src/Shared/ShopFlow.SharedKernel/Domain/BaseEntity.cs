namespace ShopFlow.SharedKernel.Domain;

/// <summary>
/// Base class for entities with an identity, tenant scope, and a domain-event buffer.
/// Mirrors Tech Design §20 (Shared Kernel) verbatim. The buffer is drained by
/// the outbox interceptor in <see cref="Infrastructure.OutboxInterceptor"/>;
/// callers must not consume <see cref="DomainEvents"/> for any other purpose.
/// </summary>
public abstract class BaseEntity
{
    public Guid Id { get; protected set; } = Guid.NewGuid();

    public Guid TenantId { get; protected set; }

    public DateTime CreatedAt { get; protected set; } = DateTime.UtcNow;

    public DateTime? UpdatedAt { get; protected set; }

    private readonly List<IDomainEvent> _domainEvents = new();

    public IReadOnlyList<IDomainEvent> DomainEvents => _domainEvents.AsReadOnly();

    protected void RaiseDomainEvent(IDomainEvent e) => _domainEvents.Add(e);

    public void ClearDomainEvents() => _domainEvents.Clear();
}
