using Microsoft.EntityFrameworkCore;
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

    public DbSet<StockItem> StockItems => Set<StockItem>();

    public DbSet<Reservation> Reservations => Set<Reservation>();

    public DbSet<StockAdjustment> StockAdjustments => Set<StockAdjustment>();

    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.ApplyConfiguration(new StockItemConfiguration());
        modelBuilder.ApplyConfiguration(new ReservationConfiguration());
        modelBuilder.ApplyConfiguration(new StockAdjustmentConfiguration());
        modelBuilder.ApplyConfiguration(new OutboxMessageConfiguration());
    }
}
