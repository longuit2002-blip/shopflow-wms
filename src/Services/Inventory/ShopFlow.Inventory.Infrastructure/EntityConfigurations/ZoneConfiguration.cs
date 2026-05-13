using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ShopFlow.Inventory.Domain;

namespace ShopFlow.Inventory.Infrastructure.EntityConfigurations;

internal sealed class ZoneConfiguration : IEntityTypeConfiguration<Zone>
{
    public void Configure(EntityTypeBuilder<Zone> builder)
    {
        builder.ToTable("zones");
        builder.HasKey(z => z.ZoneId).HasName("pk_zones");
        builder
            .Property(z => z.ZoneId)
            .HasColumnName("zone_id")
            .ValueGeneratedOnAdd();
        builder.Property(z => z.Name).HasColumnName("name").HasMaxLength(64).IsRequired();
        builder
            .Property(z => z.WarehouseId)
            .HasColumnName("warehouse_id")
            .HasMaxLength(64)
            .IsRequired();
    }
}
