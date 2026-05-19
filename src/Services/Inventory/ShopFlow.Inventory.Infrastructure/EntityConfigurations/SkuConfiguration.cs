using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ShopFlow.Inventory.Domain.Catalog;
using ShopFlow.Inventory.Domain.Catalog.ValueObjects;
using SkuCode = ShopFlow.Inventory.Domain.Sku;

namespace ShopFlow.Inventory.Infrastructure.EntityConfigurations;

/// <summary>
/// Fluent map for the Sprint-7.5 U3 <c>skus</c> table — the per-tenant
/// rich SKU catalog. Replaces the in-memory
/// <c>InMemorySkuMetadataStore</c> singleton.
/// </summary>
/// <remarks>
/// <para>The PK is the SKU string itself (<c>Code</c>); the inherited
/// <c>BaseEntity.Id</c> Guid is ignored, mirroring the
/// <c>StockItem</c> pattern. The <c>Dimensions</c> value object is
/// persisted as <c>jsonb</c> via a value converter so the column stays
/// queryable from Postgres while the Domain layer keeps strong-typed
/// access.</para>
///
/// <para>Indexes per Sprint-7.5 R2 / KTD2 — three production indexes:
/// btree on <c>category</c>, partial btree on <c>is_flash_sale</c>
/// WHERE TRUE (so flash-sale toggles do not bloat the index), and
/// partial UNIQUE on <c>barcode</c> WHERE NOT NULL (matches the
/// catalog reality that some SKUs ship without a printed barcode).</para>
/// </remarks>
internal sealed class SkuConfiguration : IEntityTypeConfiguration<Sku>
{
    public void Configure(EntityTypeBuilder<Sku> builder)
    {
        builder.ToTable("skus");

        builder.Ignore(s => s.Id);
        builder.Ignore(s => s.DomainEvents);

        builder.HasKey(s => s.Code).HasName("pk_skus");

        builder
            .Property(s => s.Code)
            .HasColumnName("sku")
            .HasMaxLength(SkuCode.MaxLength)
            .HasConversion(v => v.Value, v => SkuCode.Create(v))
            .IsRequired();

        builder
            .Property(s => s.Name)
            .HasColumnName("name")
            .HasMaxLength(Sku.MaxNameLength)
            .IsRequired();

        builder
            .Property(s => s.Category)
            .HasColumnName("category")
            .HasMaxLength(Sku.MaxCategoryLength);

        builder.Property(s => s.Threshold).HasColumnName("threshold");

        builder.Property(s => s.WeightGrams).HasColumnName("weight_grams");

        builder
            .Property(s => s.Dimensions)
            .HasColumnName("dimensions")
            .HasColumnType("jsonb")
            .HasConversion(
                v => v == null ? null : JsonSerializer.Serialize(SkuDimensionsDto.From(v), (JsonSerializerOptions?)null),
                v => string.IsNullOrEmpty(v)
                    ? null
                    : SkuDimensionsDto.ToDomain(JsonSerializer.Deserialize<SkuDimensionsDto>(v, (JsonSerializerOptions?)null))
            );

        builder
            .Property(s => s.Description)
            .HasColumnName("description")
            .HasMaxLength(Sku.MaxDescriptionLength);

        builder
            .Property(s => s.ImageUrl)
            .HasColumnName("image_url")
            .HasMaxLength(Sku.MaxImageUrlLength);

        builder
            .Property(s => s.Barcode)
            .HasColumnName("barcode")
            .HasMaxLength(Sku.MaxBarcodeLength);

        builder
            .Property(s => s.Brand)
            .HasColumnName("brand")
            .HasMaxLength(Sku.MaxBrandLength);

        builder
            .Property(s => s.IsFlashSale)
            .HasColumnName("is_flash_sale")
            .HasDefaultValue(false)
            .IsRequired();

        builder.Property(s => s.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(s => s.UpdatedAt).HasColumnName("updated_at");

        // Sprint-7.5 R2 / KTD2 — three production indexes.
        builder
            .HasIndex(s => s.Category)
            .HasDatabaseName("ix_skus_category");

        builder
            .HasIndex(s => s.IsFlashSale)
            .HasDatabaseName("ix_skus_is_flash_sale")
            .HasFilter("\"is_flash_sale\" = TRUE");

        builder
            .HasIndex(s => s.Barcode)
            .IsUnique()
            .HasDatabaseName("ux_skus_barcode")
            .HasFilter("\"barcode\" IS NOT NULL");
    }

    /// <summary>
    /// Wire shape for the <c>jsonb</c> dimensions column. Kept private
    /// so the Domain stays serialization-agnostic.
    /// </summary>
    private sealed record SkuDimensionsDto(decimal Length, decimal Width, decimal Height, string Unit)
    {
        public static SkuDimensionsDto From(SkuDimensions d) =>
            new(d.Length, d.Width, d.Height, d.Unit);

        public static SkuDimensions? ToDomain(SkuDimensionsDto? dto) =>
            dto is null ? null : SkuDimensions.Create(dto.Length, dto.Width, dto.Height, dto.Unit);
    }
}
