using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ShopFlow.Outbound.Domain;

namespace ShopFlow.Outbound.Infrastructure.EntityConfigurations;

/// <summary>
/// Fluent map for <c>pick_waves</c> per Sprint-3-redux plan R10. One
/// row per closed pick wave emitted by U5's
/// <c>PickWaveGeneratorService</c>; the per-wave assignments live in
/// <see cref="PickAssignmentConfiguration"/> with the FK back here.
/// </summary>
internal sealed class PickWaveConfiguration : IEntityTypeConfiguration<PickWave>
{
    public void Configure(EntityTypeBuilder<PickWave> builder)
    {
        builder.ToTable("pick_waves");

        builder.Ignore(w => w.DomainEvents);

        builder.HasKey(w => w.Id).HasName("pk_pick_waves");
        builder.Property(w => w.Id).HasColumnName("id");

        builder
            .Property(w => w.ShippingProfile)
            .HasColumnName("shipping_profile")
            .HasMaxLength(64)
            .IsRequired();

        builder.Property(w => w.PickerId).HasColumnName("picker_id").HasMaxLength(64).IsRequired();

        builder.Property(w => w.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(w => w.UpdatedAt).HasColumnName("updated_at");
        builder.Property(w => w.ClosedAt).HasColumnName("closed_at");

        builder
            .HasMany(w => w.Assignments)
            .WithOne()
            .HasForeignKey(a => a.PickWaveId)
            .HasConstraintName("fk_pick_assignments_pick_waves")
            .OnDelete(DeleteBehavior.Cascade);
    }
}
