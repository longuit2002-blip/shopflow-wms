using Microsoft.EntityFrameworkCore;
using ShopFlow.SharedKernel.Domain;
using ShopFlow.SharedKernel.Infrastructure;

namespace ShopFlow.SharedKernel.UnitTests.Infrastructure;

/// <summary>
/// EF Core DbContext used by the interceptor unit tests. Uses SQLite
/// in-memory so the kernel's interceptor wiring exercises real EF Core
/// (change tracking, transaction boundary) without a Postgres dependency.
/// Postgres-backed integration tests for these interceptors land in U6.
/// </summary>
internal sealed class TestDbContext : DbContext
{
    public TestDbContext(DbContextOptions<TestDbContext> options)
        : base(options) { }

    public DbSet<Widget> Widgets => Set<Widget>();
    public DbSet<OutboxMessage> Outbox => Set<OutboxMessage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Widget>(b =>
        {
            b.ToTable("widgets");
            b.HasKey(w => w.Id);
            b.Property(w => w.Name).HasMaxLength(64).IsRequired();
            b.Property(w => w.TenantId).IsRequired();
        });

        modelBuilder.Entity<OutboxMessage>(b =>
        {
            b.ToTable("outbox_messages");
            b.HasKey(o => o.Id);
            b.Property(o => o.EventType).IsRequired();
            b.Property(o => o.Payload).IsRequired();
        });
    }
}

/// <summary>
/// Concrete <see cref="BaseEntity"/> for fixture purposes. Subclassable so
/// individual tests can opt out of the canonical constructor (e.g. to leave
/// TenantId unset and exercise the interceptor's "stamp from request context"
/// path). Never referenced by production code.
/// </summary>
internal class Widget : BaseEntity
{
    protected Widget() { }

    public Widget(Guid tenantId, string name)
    {
        TenantId = tenantId;
        Name = name;
    }

    public string Name { get; private set; } = string.Empty;

    public void RaiseTestEvent() => RaiseDomainEvent(new WidgetChangedEvent(TenantId, Name));
}

internal sealed record WidgetChangedEvent(Guid TenantId, string Name) : IDomainEvent
{
    public DateTime OccurredAt { get; } = DateTime.UtcNow;
}
