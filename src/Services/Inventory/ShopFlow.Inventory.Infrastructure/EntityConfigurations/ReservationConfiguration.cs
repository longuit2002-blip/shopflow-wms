using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ShopFlow.Inventory.Domain;
using ShopFlow.SharedKernel.Application;

namespace ShopFlow.Inventory.Infrastructure.EntityConfigurations;

/// <summary>
/// Maps <see cref="Reservation"/> to <c>reservations_ledger</c> per Tech
/// Design §7.2. Primary key is <c>(tenant_id, sku, id)</c> — the same shape
/// the conditional-INSERT CTE relies on. Unique constraint on
/// <c>(tenant_id, order_id)</c> drives the application-layer idempotency
/// short-circuit. The partial covering index from §7.3
/// (<c>idx_active_reservations</c>) requires <c>INCLUDE</c>, which EF Core
/// 8 cannot express; it lives in the migration's raw SQL.
/// </summary>
internal sealed class ReservationConfiguration : IEntityTypeConfiguration<Reservation>
{
    private readonly IRequestContext _requestContext;

    public ReservationConfiguration(IRequestContext requestContext)
    {
        _requestContext = requestContext;
    }

    public void Configure(EntityTypeBuilder<Reservation> builder)
    {
        builder.ToTable("reservations_ledger");

        builder.HasKey(e => new
        {
            e.TenantId,
            e.Sku,
            e.Id,
        });

        builder.Property(e => e.Id).HasColumnName("id").ValueGeneratedNever();

        builder.Property(e => e.TenantId).HasColumnName("tenant_id").IsRequired();

        builder.Property(e => e.Sku).HasColumnName("sku").HasMaxLength(64).IsRequired();

        builder.Property(e => e.Qty).HasColumnName("qty").IsRequired();

        builder.Property(e => e.OrderId).HasColumnName("order_id").IsRequired();

        builder
            .Property(e => e.Status)
            .HasColumnName("status")
            .HasConversion<string>()
            .HasMaxLength(16)
            .IsRequired();

        builder.Property(e => e.ReservedAt).HasColumnName("reserved_at").IsRequired();

        builder.Property(e => e.ExpiresAt).HasColumnName("expires_at").IsRequired();

        builder.Property(e => e.FinalizedAt).HasColumnName("finalized_at");

        builder
            .HasIndex(e => new { e.TenantId, e.OrderId })
            .IsUnique()
            .HasDatabaseName("ux_reservations_tenant_order");

        builder.HasQueryFilter(e => e.TenantId == _requestContext.TenantId);
    }
}
