using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using ShopFlow.Inventory.Domain;
using ShopFlow.Inventory.Infrastructure.EntityConfigurations;
using ShopFlow.SharedKernel.Infrastructure;

namespace ShopFlow.Inventory.Infrastructure;

/// <summary>
/// EF Core context for one tenant's Inventory schema. Per AGENTS.md §3.17
/// + §11.80 instances are constructed only via
/// <see cref="Microsoft.EntityFrameworkCore.IDbContextFactory{TContext}"/>
/// — the factory reads <c>IRequestContext.DbConnectionString</c> at
/// scope entry. Direct <c>new InventoryDbContext(...)</c> is forbidden in
/// business code (ShopFlow0003 analyzer enforces).
/// </summary>
/// <remarks>
/// <para>Schema per Tech Design v3.0 §4.2 — three tables plus the
/// per-tenant outbox:</para>
/// <list type="bullet">
///   <item><description><c>stock_items</c> — SKU PK, available + reserved + xid row_version.</description></item>
///   <item><description><c>reservations_ledger</c> — id PK, sku FK, UNIQUE(order_id).</description></item>
///   <item><description><c>stock_adjustments</c> — audit trail for non-reservation deltas.</description></item>
///   <item><description><c>outbox_messages</c> — per-tenant outbox consumed by the multiplexed dispatcher (SharedKernel §5).</description></item>
/// </list>
/// <para>None of the business tables carry <c>tenant_id</c> per ADR-0003;
/// the database identity is the tenant boundary.</para>
/// </remarks>
public sealed class InventoryDbContext : DbContext
{
    public InventoryDbContext(DbContextOptions<InventoryDbContext> options)
        : base(options) { }

    /// <summary>
    /// Suppress EF Core 9's <see cref="RelationalEventId.PendingModelChangesWarning"/>.
    /// Hand-authored migrations (AGENTS.md §3.23) do not ship the
    /// <c>InventoryDbContextModelSnapshot.cs</c> file that EF generates via
    /// <c>dotnet ef migrations add</c>; without that snapshot, EF compares
    /// the runtime model against an empty baseline and surfaces the entire
    /// model as "pending changes". The warning is a structural false
    /// positive under the hand-authored pattern, not a real signal of drift.
    /// Schema correctness is enforced by the integration smoke tests
    /// (<c>MigrationSmokeTests</c>) which assert named tables / constraints /
    /// indexes after <c>MigrateAsync()</c>. See
    /// <c>docs/solutions/2026-05-13-ef9-pendingmodelchangeswarning-with-hand-authored-migrations.md</c>.
    /// </summary>
    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        base.OnConfiguring(optionsBuilder);
        optionsBuilder.ConfigureWarnings(w =>
            w.Ignore(RelationalEventId.PendingModelChangesWarning)
        );
    }

    public DbSet<StockItem> StockItems => Set<StockItem>();

    public DbSet<Reservation> Reservations => Set<Reservation>();

    public DbSet<StockAdjustment> StockAdjustments => Set<StockAdjustment>();

    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    public DbSet<Zone> Zones => Set<Zone>();

    public DbSet<Bin> Bins => Set<Bin>();

    public DbSet<StockItemBin> StockItemBins => Set<StockItemBin>();

    public DbSet<InboundDedup> InboundDedup => Set<InboundDedup>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.ApplyConfiguration(new StockItemConfiguration());
        modelBuilder.ApplyConfiguration(new ReservationConfiguration());
        modelBuilder.ApplyConfiguration(new StockAdjustmentConfiguration());
        modelBuilder.ApplyConfiguration(new OutboxMessageConfiguration());
        modelBuilder.ApplyConfiguration(new ZoneConfiguration());
        modelBuilder.ApplyConfiguration(new BinConfiguration());
        modelBuilder.ApplyConfiguration(new StockItemBinConfiguration());
        modelBuilder.ApplyConfiguration(new InboundDedupConfiguration());
    }
}
