using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ShopFlow.Outbound.Domain;

namespace ShopFlow.Outbound.Infrastructure.EntityConfigurations;

/// <summary>
/// Fluent map for <c>orders</c> per Sprint-3-redux plan R2. Idempotency
/// anchor is <c>UNIQUE(channel_external_order_id)</c> — duplicate POSTs
/// hit the index, the controller short-circuits with the existing
/// <see cref="Order.Id"/>. Per-line FK config lives on
/// <see cref="OrderLineConfiguration"/>; <c>pick_wave_id</c> is a
/// nullable FK populated post-creation by U5's wave generator.
/// </summary>
internal sealed class OrderConfiguration : IEntityTypeConfiguration<Order>
{
    public void Configure(EntityTypeBuilder<Order> builder)
    {
        builder.ToTable("orders");

        builder.Ignore(o => o.DomainEvents);

        builder.HasKey(o => o.Id).HasName("pk_orders");
        builder.Property(o => o.Id).HasColumnName("id");

        builder
            .Property(o => o.ChannelExternalOrderId)
            .HasColumnName("channel_external_order_id")
            .HasMaxLength(128)
            .IsRequired();

        builder
            .Property(o => o.ShippingProfile)
            .HasColumnName("shipping_profile")
            .HasMaxLength(64)
            .IsRequired();

        builder
            .Property(o => o.Status)
            .HasColumnName("status")
            .HasMaxLength(32)
            .HasConversion<string>()
            .IsRequired();

        builder.Property(o => o.ExpectedWeightTotal).HasColumnName("expected_weight_total");
        builder.Property(o => o.ActualWeightTotal).HasColumnName("actual_weight_total");
        builder.Property(o => o.LabelUrl).HasColumnName("label_url").HasMaxLength(512);
        builder
            .Property(o => o.TrackingNumber)
            .HasColumnName("tracking_number")
            .HasMaxLength(128);
        builder.Property(o => o.PickWaveId).HasColumnName("pick_wave_id");

        builder.Property(o => o.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(o => o.UpdatedAt).HasColumnName("updated_at");

        builder
            .HasMany(o => o.Lines)
            .WithOne()
            .HasForeignKey(l => l.OrderId)
            .HasConstraintName("fk_order_lines_orders")
            .OnDelete(DeleteBehavior.Cascade);

        builder
            .HasIndex(o => o.ChannelExternalOrderId)
            .IsUnique()
            .HasDatabaseName("ux_orders_channel_external_order_id");

        builder.HasIndex(o => o.Status).HasDatabaseName("ix_orders_status");
    }
}
