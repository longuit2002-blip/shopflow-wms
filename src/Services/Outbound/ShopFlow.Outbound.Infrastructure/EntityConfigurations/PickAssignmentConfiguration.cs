using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ShopFlow.Outbound.Domain;

namespace ShopFlow.Outbound.Infrastructure.EntityConfigurations;

/// <summary>
/// Fluent map for <c>pick_assignments</c> per Sprint-3-redux plan R10.
/// Bridges a <see cref="PickWave"/> with its referenced
/// <see cref="Order"/> rows; the FK back to <c>pick_waves</c> is
/// configured on <see cref="PickWaveConfiguration"/>'s <c>HasMany</c>.
/// The <c>order_id</c> FK back to <c>orders</c> uses <c>Restrict</c>
/// — deleting an order on the rare admin path is blocked while a
/// pick assignment references it.
/// </summary>
internal sealed class PickAssignmentConfiguration : IEntityTypeConfiguration<PickAssignment>
{
    public void Configure(EntityTypeBuilder<PickAssignment> builder)
    {
        builder.ToTable("pick_assignments");

        builder.Ignore(a => a.DomainEvents);

        builder.HasKey(a => a.Id).HasName("pk_pick_assignments");
        builder.Property(a => a.Id).HasColumnName("id");

        builder.Property(a => a.PickWaveId).HasColumnName("pick_wave_id").IsRequired();
        builder.Property(a => a.OrderId).HasColumnName("order_id").IsRequired();

        builder.Property(a => a.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(a => a.UpdatedAt).HasColumnName("updated_at");

        builder
            .HasOne<Order>()
            .WithMany()
            .HasForeignKey(a => a.OrderId)
            .HasConstraintName("fk_pick_assignments_orders")
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(a => a.OrderId).HasDatabaseName("ix_pick_assignments_order_id");
    }
}
