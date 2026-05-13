using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using ShopFlow.Channel.Domain.ProductMappings;
using ShopFlow.Channel.Domain.Webhooks;
using ShopFlow.Channel.Infrastructure.EntityConfigurations;
using ShopFlow.SharedKernel.Infrastructure;
using ChannelAggregate = ShopFlow.Channel.Domain.Channels.Channel;

namespace ShopFlow.Channel.Infrastructure;

/// <summary>
/// EF Core context for one tenant's Channel schema per Sprint-4 plan U2.
/// Constructed per request via <see cref="IDbContextFactory{TContext}"/>
/// bound to <c>IRequestContext.DbConnectionString</c> (AGENTS.md §3.17).
/// Direct <c>new ChannelDbContext(...)</c> outside a *Factory / *Tests /
/// *Fixture type is forbidden by <c>ShopFlow0003</c>.
/// </summary>
/// <remarks>
/// <para>Schema per Sprint-4 plan R3/R6/R9 — four module-owned tables:</para>
/// <list type="bullet">
///   <item><description><c>channels</c> — denormalized adapter-routing projection of <c>control_plane.channel_connections</c>.</description></item>
///   <item><description><c>webhook_events</c> — <c>UNIQUE(channel_id, provider_event_id)</c> is the idempotency anchor (R3).</description></item>
///   <item><description><c>product_mappings</c> — <c>UNIQUE(channel_id, external_sku)</c> single-mapping-per-channel.</description></item>
///   <item><description><c>channel_outbox_messages</c> — per-tenant outbox consumed by <see cref="MultiplexedOutboxDispatcher{TContext}"/>. Per-module prefix per Sprint-2.5.</description></item>
/// </list>
/// <para>None of these tables carries a <c>tenant_id</c> column per ADR-0003 —
/// the database identity is the tenant boundary.</para>
/// </remarks>
public sealed class ChannelDbContext : DbContext
{
    public ChannelDbContext(DbContextOptions<ChannelDbContext> options)
        : base(options) { }

    /// <summary>
    /// Suppress EF Core 9's <see cref="RelationalEventId.PendingModelChangesWarning"/>
    /// — hand-authored migrations (AGENTS.md §3.23) ship without the
    /// <c>ModelSnapshot</c> companion <c>dotnet ef migrations add</c> emits.
    /// Schema correctness is enforced by <c>MigrationSmokeTests</c>. See
    /// <c>docs/solutions/2026-05-13-ef9-pendingmodelchangeswarning-with-hand-authored-migrations.md</c>.
    /// </summary>
    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        base.OnConfiguring(optionsBuilder);
        optionsBuilder.ConfigureWarnings(w =>
            w.Ignore(RelationalEventId.PendingModelChangesWarning)
        );
    }

    public DbSet<ChannelAggregate> Channels => Set<ChannelAggregate>();

    public DbSet<WebhookEvent> WebhookEvents => Set<WebhookEvent>();

    public DbSet<ProductMapping> ProductMappings => Set<ProductMapping>();

    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.ApplyConfiguration(new ChannelConfiguration());
        modelBuilder.ApplyConfiguration(new WebhookEventConfiguration());
        modelBuilder.ApplyConfiguration(new ProductMappingConfiguration());
        modelBuilder.ApplyConfiguration(new OutboxMessageConfiguration());
    }
}
