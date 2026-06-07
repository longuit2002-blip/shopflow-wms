using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ShopFlow.Outbound.Domain;

namespace ShopFlow.Outbound.Infrastructure.EntityConfigurations;

/// <summary>
/// Fluent map for the <c>pickers</c> reference-data table per
/// Sprint-3-redux plan R10. Operator-seeded for MVP; load tests seed
/// 5 pickers per tenant via raw SQL. Text PK (<c>picker_id</c>) keeps
/// the round-robin ordering stable + human-readable.
/// </summary>
internal sealed class PickerConfiguration : IEntityTypeConfiguration<Picker>
{
    public void Configure(EntityTypeBuilder<Picker> builder)
    {
        builder.ToTable("pickers");

        builder.HasKey(p => p.PickerId).HasName("pk_pickers");

        builder.Property(p => p.PickerId).HasColumnName("picker_id").HasMaxLength(64).IsRequired();

        builder
            .Property(p => p.DisplayName)
            .HasColumnName("display_name")
            .HasMaxLength(128)
            .IsRequired();
    }
}
