using Microsoft.EntityFrameworkCore;
using ShopFlow.ControlPlane.Domain;
using ShopFlow.ControlPlane.Infrastructure.EntityConfigurations;

namespace ShopFlow.ControlPlane.Infrastructure;

/// <summary>
/// EF Core context for <c>shopflow_control</c>. Unlike per-tenant
/// <c>DbContext</c>s (constructed via <see cref="Microsoft.EntityFrameworkCore.IDbContextFactory{TContext}"/>
/// per request, AGENTS.md §3.17), the control-plane context has a fixed
/// connection string wired at composition-root time via
/// <c>services.AddDbContext&lt;ControlPlaneDbContext&gt;(...)</c>. Carrying
/// the catalog DB connection through <c>IRequestContext</c> would defeat
/// the whole point — the routing middleware is the consumer.
/// </summary>
/// <remarks>
/// Schema is the <c>tenants</c> + <c>tenant_events</c> + <c>channel_connections</c>
/// triple per Tech Design v3.0 §1.5. Migrations live in
/// <c>ShopFlow.ControlPlane.Migrations</c> and target the assembly name
/// declared in <c>UseNpgsql(... b =&gt; b.MigrationsAssembly(...))</c> at the
/// composition root.
/// </remarks>
public sealed class ControlPlaneDbContext : DbContext
{
    public ControlPlaneDbContext(DbContextOptions<ControlPlaneDbContext> options)
        : base(options) { }

    public DbSet<Tenant> Tenants => Set<Tenant>();

    public DbSet<TenantEvent> TenantEvents => Set<TenantEvent>();

    public DbSet<ChannelConnection> ChannelConnections => Set<ChannelConnection>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.ApplyConfiguration(new TenantConfiguration());
        modelBuilder.ApplyConfiguration(new TenantEventConfiguration());
        modelBuilder.ApplyConfiguration(new ChannelConnectionConfiguration());
    }
}
