using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using ShopFlow.Inbound.Domain;
using ShopFlow.Inbound.Infrastructure.EntityConfigurations;
using ShopFlow.SharedKernel.Infrastructure;

namespace ShopFlow.Inbound.Infrastructure;

/// <summary>
/// EF Core context for one tenant's Inbound schema. Constructed per request
/// via <see cref="IDbContextFactory{TContext}"/> bound to
/// <c>IRequestContext.DbConnectionString</c> (AGENTS.md §3.17). Direct
/// <c>new InboundDbContext(...)</c> outside a *Factory / *Tests / *Fixture
/// type is forbidden by <c>ShopFlow0003</c>.
/// </summary>
/// <remarks>
/// <para>Schema per Sprint-2-redux plan (R3, R5, R9):</para>
/// <list type="bullet">
///   <item><description><c>purchase_orders</c> + <c>purchase_order_lines</c> — PO aggregate.</description></item>
///   <item><description><c>receivings</c> + <c>receiving_lines</c> — Receiving aggregate; <c>UNIQUE(receiving_id, purchase_order_line_id)</c> idempotency anchor.</description></item>
///   <item><description><c>reconciliation_tickets</c> — append-only ticket log; resolution flow deferred.</description></item>
///   <item><description><c>outbox_messages</c> — per-tenant outbox consumed by <c>MultiplexedOutboxDispatcher&lt;InboundDbContext&gt;</c>.</description></item>
/// </list>
/// <para>None of these tables carries a <c>tenant_id</c> column per ADR-0003;
/// the database identity is the tenant boundary.</para>
/// </remarks>
public sealed class InboundDbContext : DbContext
{
    public InboundDbContext(DbContextOptions<InboundDbContext> options)
        : base(options) { }

    /// <summary>
    /// Suppress EF Core 9's <see cref="RelationalEventId.PendingModelChangesWarning"/>.
    /// Hand-authored migrations (AGENTS.md §3.23) do not ship the
    /// <c>InboundDbContextModelSnapshot.cs</c> companion that <c>dotnet ef
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

    public DbSet<PurchaseOrder> PurchaseOrders => Set<PurchaseOrder>();

    public DbSet<PurchaseOrderLine> PurchaseOrderLines => Set<PurchaseOrderLine>();

    public DbSet<Receiving> Receivings => Set<Receiving>();

    public DbSet<ReceivingLine> ReceivingLines => Set<ReceivingLine>();

    public DbSet<ReconciliationTicket> ReconciliationTickets => Set<ReconciliationTicket>();

    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.ApplyConfiguration(new PurchaseOrderConfiguration());
        modelBuilder.ApplyConfiguration(new PurchaseOrderLineConfiguration());
        modelBuilder.ApplyConfiguration(new ReceivingConfiguration());
        modelBuilder.ApplyConfiguration(new ReceivingLineConfiguration());
        modelBuilder.ApplyConfiguration(new ReconciliationTicketConfiguration());
        modelBuilder.ApplyConfiguration(new OutboxMessageConfiguration());
    }
}
