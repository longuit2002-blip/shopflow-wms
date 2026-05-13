using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ShopFlow.Outbound.Domain;

namespace ShopFlow.Outbound.Infrastructure.EntityConfigurations;

/// <summary>
/// Fluent map for <c>order_lines</c> per Sprint-3-redux plan R2. Child
/// of the <see cref="Order"/> aggregate; the <c>OrderId</c> FK is
/// configured on <see cref="OrderConfiguration"/>'s <c>HasMany</c>
/// declaration. The line id (Guid PK) is the <c>order_line_id</c>
/// token shipped to the Inventory ledger's composite UNIQUE
/// <c>(order_id, order_line_id)</c> per K10/K11.
/// </summary>
internal sealed class OrderLineConfiguration : IEntityTypeConfiguration<OrderLine>
{
    public void Configure(EntityTypeBuilder<OrderLine> builder)
    {
        builder.ToTable("order_lines");

        builder.Ignore(l => l.DomainEvents);

        builder.HasKey(l => l.Id).HasName("pk_order_lines");
        builder.Property(l => l.Id).HasColumnName("id");

        builder.Property(l => l.OrderId).HasColumnName("order_id").IsRequired();
        builder.Property(l => l.Sku).HasColumnName("sku").HasMaxLength(64).IsRequired();
        builder.Property(l => l.Qty).HasColumnName("qty").IsRequired();
        builder.Property(l => l.ExpectedWeight).HasColumnName("expected_weight");

        builder.Property(l => l.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(l => l.UpdatedAt).HasColumnName("updated_at");

        builder.HasIndex(l => new { l.OrderId, l.Sku }).HasDatabaseName("ix_order_lines_order_id_sku");
    }
}
