using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using ShopFlow.Outbound.Application.Sagas;
using ShopFlow.Outbound.Domain;
using ShopFlow.Outbound.Infrastructure.EntityConfigurations;
using ShopFlow.SharedKernel.Infrastructure;

namespace ShopFlow.Outbound.Infrastructure;

/// <summary>
/// EF Core context for one tenant's Outbound schema. Constructed per
/// request via <see cref="IDbContextFactory{TContext}"/> bound to
/// <c>IRequestContext.DbConnectionString</c> (AGENTS.md §3.17). Direct
/// <c>new OutboundDbContext(...)</c> outside a *Factory / *Tests / *Fixture
/// type is forbidden by <c>ShopFlow0003</c>.
/// </summary>
/// <remarks>
/// <para>Schema per Sprint-3-redux plan R2 — 6 module-owned tables plus
/// MassTransit's <c>saga_state</c> (U4 wires the saga repo against it):</para>
/// <list type="bullet">
///   <item><description><c>orders</c> + <c>order_lines</c> — Order aggregate (U2). <c>UNIQUE(channel_external_order_id)</c> idempotency anchor.</description></item>
///   <item><description><c>pick_waves</c> + <c>pick_assignments</c> — PickWave aggregate (U5).</description></item>
///   <item><description><c>pickers</c> — reference data (U5).</description></item>
///   <item><description><c>saga_state</c> — managed by MassTransit's EF saga repository (U4); no DbSet here because MT's repo handles the mapping directly via its own configuration.</description></item>
///   <item><description><c>outbound_outbox_messages</c> — per-tenant outbox consumed by <c>MultiplexedOutboxDispatcher&lt;OutboundDbContext&gt;</c>. Per-module prefix per Sprint-2.5.</description></item>
/// </list>
/// <para>None of these tables carries a <c>tenant_id</c> column per ADR-0003;
/// the database identity is the tenant boundary.</para>
/// </remarks>
public sealed class OutboundDbContext : DbContext
{
    public OutboundDbContext(DbContextOptions<OutboundDbContext> options)
        : base(options) { }

    /// <summary>
    /// Suppress EF Core 9's <see cref="RelationalEventId.PendingModelChangesWarning"/>.
    /// Hand-authored migrations (AGENTS.md §3.23) do not ship the
    /// <c>OutboundDbContextModelSnapshot.cs</c> companion that <c>dotnet ef
    /// migrations add</c> emits; without that snapshot EF compares the
    /// runtime model against an empty baseline and surfaces the entire
    /// model as "pending changes". Schema correctness is enforced by
    /// <c>MigrationSmokeTests</c>. See
    /// <c>docs/solutions/2026-05-13-ef9-pendingmodelchangeswarning-with-hand-authored-migrations.md</c>.
    /// </summary>
    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        base.OnConfiguring(optionsBuilder);
        optionsBuilder.ConfigureWarnings(w =>
            w.Ignore(RelationalEventId.PendingModelChangesWarning)
        );
    }

    public DbSet<Order> Orders => Set<Order>();

    public DbSet<OrderLine> OrderLines => Set<OrderLine>();

    public DbSet<PickWave> PickWaves => Set<PickWave>();

    public DbSet<PickAssignment> PickAssignments => Set<PickAssignment>();

    public DbSet<Picker> Pickers => Set<Picker>();

    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    /// <summary>
    /// Sprint-7 R14 — append-only audit of saga state transitions. Written
    /// by <c>SagaTransitionObserver : IStateObserver&lt;FulfillmentSagaState&gt;</c>;
    /// read by the Orders detail route's transitions endpoint.
    /// </summary>
    public DbSet<OrderTransition> OrderTransitions => Set<OrderTransition>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.ApplyConfiguration(new OrderConfiguration());
        modelBuilder.ApplyConfiguration(new OrderLineConfiguration());
        modelBuilder.ApplyConfiguration(new PickWaveConfiguration());
        modelBuilder.ApplyConfiguration(new PickAssignmentConfiguration());
        modelBuilder.ApplyConfiguration(new PickerConfiguration());
        modelBuilder.ApplyConfiguration(new OutboxMessageConfiguration());
        modelBuilder.ApplyConfiguration(new OrderTransitionConfiguration());

        // U4: register the MassTransit saga state mapping so MT's EF saga
        // repository (configured via .ExistingDbContext<OutboundDbContext>()
        // in AddOutboundModule) finds the saga_state table. No DbSet is
        // exposed — MT's repository owns the read/write path.
        modelBuilder.ApplyConfiguration(new FulfillmentSagaStateConfiguration());
    }
}
