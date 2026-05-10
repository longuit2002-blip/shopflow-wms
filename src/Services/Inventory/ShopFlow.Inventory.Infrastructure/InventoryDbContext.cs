using Microsoft.EntityFrameworkCore;
using ShopFlow.Inventory.Domain;
using ShopFlow.Inventory.Infrastructure.EntityConfigurations;
using ShopFlow.SharedKernel.Application;
using ShopFlow.SharedKernel.Infrastructure;

namespace ShopFlow.Inventory.Infrastructure;

/// <summary>
/// EF Core DbContext for the Inventory module. Owns the
/// <c>stock_items</c>, <c>reservations_ledger</c>,
/// <c>stock_adjustments</c>, and <c>outbox_messages</c> tables. Per Tech
/// Design §4 every read is filtered by the active tenant via a global query
/// filter; the kernel's <see cref="TenancyInterceptor"/> guards writes.
/// </summary>
public sealed class InventoryDbContext : DbContext
{
    private readonly IRequestContext _requestContext;

    public InventoryDbContext(
        DbContextOptions<InventoryDbContext> options,
        IRequestContext requestContext
    )
        : base(options)
    {
        _requestContext = requestContext;
    }

    public DbSet<StockItem> StockItems => Set<StockItem>();

    public DbSet<Reservation> Reservations => Set<Reservation>();

    public DbSet<StockAdjustmentRecord> StockAdjustments => Set<StockAdjustmentRecord>();

    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.ApplyConfiguration(new StockItemConfiguration(_requestContext));
        modelBuilder.ApplyConfiguration(new ReservationConfiguration(_requestContext));
        modelBuilder.ApplyConfiguration(new StockAdjustmentRecordConfiguration(_requestContext));
        modelBuilder.ApplyConfiguration(new OutboxMessageConfiguration());
    }
}

/// <summary>
/// Audit row inserted alongside <see cref="StockItem.AdjustStock"/>. Lives
/// in Infrastructure (rather than Domain) because the audit log is purely
/// a read-side concern — the domain event <c>StockAdjustedEvent</c> carries
/// the same data into the outbox.
/// </summary>
public sealed class StockAdjustmentRecord
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid TenantId { get; set; }

    public Guid StockItemId { get; set; }

    public int QuantityDelta { get; set; }

    public StockAdjustmentReason Reason { get; set; }

    public Guid UserId { get; set; }

    public string? Notes { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
