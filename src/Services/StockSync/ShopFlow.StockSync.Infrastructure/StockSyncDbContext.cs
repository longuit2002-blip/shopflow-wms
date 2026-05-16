using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using ShopFlow.SharedKernel.Infrastructure;
using ShopFlow.StockSync.Domain.Aggregates;
using ShopFlow.StockSync.Infrastructure.EntityConfigurations;

namespace ShopFlow.StockSync.Infrastructure;

/// <summary>
/// EF Core context for one tenant's StockSync schema (Sprint-5 plan U1).
/// Constructed per request via <see cref="IDbContextFactory{TContext}"/>
/// bound to <c>IRequestContext.DbConnectionString</c>, mirroring the
/// Sprint-4 <c>ChannelDbContext</c> shape.
/// </summary>
/// <remarks>
/// <para>Three module-owned tables (Sprint-5 plan U1 + Sprint-2.5 per-module
/// outbox prefix):</para>
/// <list type="bullet">
///   <item><description><c>stock_sync_sku_flag</c> — per-SKU is_flash_sale flag (R10/U7).</description></item>
///   <item><description><c>stock_sync_push_log</c> — audit row per push attempt (R12/U5); UNIQUE(idempotency_key) catches MT redelivery.</description></item>
///   <item><description><c>stock_sync_outbox_messages</c> — per-tenant outbox; placeholder for cross-module events StockSync may emit later (Phase-3).</description></item>
/// </list>
/// <para>Per ADR-0003 no business table carries <c>tenant_id</c>. The
/// outbox does, because the multiplexed dispatcher needs to scope its
/// query without opening a tenant-specific connection per row.</para>
/// </remarks>
public sealed class StockSyncDbContext : DbContext
{
    public StockSyncDbContext(DbContextOptions<StockSyncDbContext> options)
        : base(options) { }

    /// <summary>
    /// Suppress EF Core 9's <see cref="RelationalEventId.PendingModelChangesWarning"/>
    /// — hand-authored migrations (AGENTS.md §3.23) ship without the
    /// <c>ModelSnapshot</c> companion <c>dotnet ef migrations add</c> emits.
    /// Schema correctness is enforced by <c>MigrationSmokeTests</c>.
    /// </summary>
    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        base.OnConfiguring(optionsBuilder);
        optionsBuilder.ConfigureWarnings(w =>
            w.Ignore(RelationalEventId.PendingModelChangesWarning)
        );
    }

    public DbSet<SkuFlag> SkuFlags => Set<SkuFlag>();

    public DbSet<PushLogEntry> PushLogEntries => Set<PushLogEntry>();

    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.ApplyConfiguration(new SkuFlagConfiguration());
        modelBuilder.ApplyConfiguration(new PushLogEntryConfiguration());
        modelBuilder.ApplyConfiguration(new OutboxMessageConfiguration());
    }
}
