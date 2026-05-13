using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ShopFlow.Inventory.Domain;

namespace ShopFlow.Inventory.Infrastructure.EntityConfigurations;

internal sealed class BinConfiguration : IEntityTypeConfiguration<Bin>
{
    public void Configure(EntityTypeBuilder<Bin> builder)
    {
        builder.ToTable("bins");
        builder.Ignore(b => b.AvailableCapacity);
        builder.HasKey(b => b.BinId).HasName("pk_bins");
        builder.Property(b => b.BinId).HasColumnName("bin_id").ValueGeneratedOnAdd();
        builder.Property(b => b.ZoneId).HasColumnName("zone_id").IsRequired();
        builder.Property(b => b.Name).HasColumnName("name").HasMaxLength(64).IsRequired();
        builder.Property(b => b.Capacity).HasColumnName("capacity").IsRequired();
        builder.Property(b => b.OccupancyQty).HasColumnName("occupancy_qty").IsRequired();

        builder
            .HasOne<Zone>()
            .WithMany()
            .HasForeignKey(b => b.ZoneId)
            .HasConstraintName("fk_bins_zones")
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(b => b.ZoneId).HasDatabaseName("ix_bins_zone_id");
    }
}
