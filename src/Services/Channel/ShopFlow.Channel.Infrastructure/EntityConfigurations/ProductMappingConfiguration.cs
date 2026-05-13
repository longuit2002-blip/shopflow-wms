using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ShopFlow.Channel.Domain.ProductMappings;

namespace ShopFlow.Channel.Infrastructure.EntityConfigurations;

/// <summary>
/// Fluent map for <c>product_mappings</c> per Sprint-4 plan U2/U6. The
/// <c>ux_product_mappings_channel_external_sku</c> UNIQUE index enforces
/// one mapping per (channel, external_sku) — idempotent admin POSTs catch
/// 23505 like Sprint-1-redux's reservation insert.
/// </summary>
internal sealed class ProductMappingConfiguration : IEntityTypeConfiguration<ProductMapping>
{
    public void Configure(EntityTypeBuilder<ProductMapping> builder)
    {
        builder.ToTable(
            "product_mappings",
            tb =>
                tb.HasCheckConstraint(
                    "ck_product_mappings_method",
                    "mapping_method IN ('Exact', 'Fuzzy', 'Manual')"
                )
        );

        builder.Ignore(m => m.DomainEvents);

        builder.HasKey(m => m.Id).HasName("pk_product_mappings");
        builder.Property(m => m.Id).HasColumnName("id");

        builder.Property(m => m.ChannelId).HasColumnName("channel_id").IsRequired();

        builder
            .Property(m => m.ExternalSku)
            .HasColumnName("external_sku")
            .HasConversion(
                vo => vo.Value,
                str => ExternalSku.Create(str).Value!
            )
            .HasMaxLength(ExternalSku.MaxLength)
            .IsRequired();

        builder
            .Property(m => m.InternalSku)
            .HasColumnName("internal_sku")
            .HasMaxLength(64)
            .IsRequired();

        builder
            .Property(m => m.ConfidenceScore)
            .HasColumnName("confidence_score")
            .HasColumnType("numeric(3,2)")
            .IsRequired();

        builder
            .Property(m => m.Method)
            .HasColumnName("mapping_method")
            .HasMaxLength(16)
            .HasConversion<string>()
            .IsRequired();

        builder.Property(m => m.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(m => m.UpdatedAt).HasColumnName("updated_at");

        builder
            .HasIndex(m => new { m.ChannelId, m.ExternalSku })
            .IsUnique()
            .HasDatabaseName("ux_product_mappings_channel_external_sku");
    }
}
