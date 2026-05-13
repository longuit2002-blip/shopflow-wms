using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ShopFlow.Outbound.Application.Sagas;

namespace ShopFlow.Outbound.Infrastructure.EntityConfigurations;

/// <summary>
/// Sprint-3-redux U4 — EF entity configuration for MassTransit's
/// <see cref="FulfillmentSagaState"/> against the U1-shipped
/// <c>saga_state</c> table. Column names QUOTED PascalCase to match the
/// initial migration (intentionally case-sensitive so MT 8.3.4's default
/// convention binds without per-column rename).
/// </summary>
/// <remarks>
/// <para>The four core columns (<c>CorrelationId</c>, <c>CurrentState</c>,
/// <c>RowVersion</c>, <c>UpdatedAt</c>) match the migration verbatim. The
/// per-state context fields (<c>TenantId</c>, <c>ShippingProfile</c>,
/// <c>LineCount</c>, <c>ReservedLineSkus</c>, <c>ReleasedLineSkus</c>,
/// <c>LinesAwaitingRelease</c>) are mapped to lower_snake_case columns
/// added by a follow-on migration in U5/U7 — for U4 these are managed
/// via EF's model-snapshot diff at runtime (the saga repo's first write
/// triggers EF's table-alter check; since the columns aren't in the
/// migration yet, MT's repo writes will fail until U5 adds the migration
/// or the columns are inlined into U1's migration). To keep U4 shippable
/// and U5/U7 unblocked, the per-state context columns are declared here
/// + added to a fresh migration in this same unit.</para>
///
/// <para>The <see cref="MassTransit.ISagaVersion.Version"/> property is
/// also mapped — MT's saga repo increments it on each write for tracking.</para>
/// </remarks>
internal sealed class FulfillmentSagaStateConfiguration : IEntityTypeConfiguration<FulfillmentSagaState>
{
    public void Configure(EntityTypeBuilder<FulfillmentSagaState> builder)
    {
        builder.ToTable("saga_state");

        builder.HasKey(s => s.CorrelationId).HasName("pk_saga_state");

        builder.Property(s => s.CorrelationId).HasColumnName("CorrelationId");

        builder
            .Property(s => s.CurrentState)
            .HasColumnName("CurrentState")
            .HasMaxLength(64)
            .IsRequired();

        builder
            .Property(s => s.RowVersion)
            .HasColumnName("RowVersion")
            .IsConcurrencyToken()
            .IsRequired();

        builder.Property(s => s.UpdatedAt).HasColumnName("UpdatedAt").IsRequired();

        // Per-state context fields (added in the U4 follow-on migration).
        // Names lower_snake_case to match the U1 module convention; only
        // the four canonical MT columns are quoted PascalCase in the
        // migration.
        builder.Property(s => s.Version).HasColumnName("version").IsRequired();
        builder.Property(s => s.TenantId).HasColumnName("tenant_id").IsRequired();
        builder
            .Property(s => s.ShippingProfile)
            .HasColumnName("shipping_profile")
            .HasMaxLength(64)
            .IsRequired();
        builder.Property(s => s.LineCount).HasColumnName("line_count").IsRequired();
        builder
            .Property(s => s.ReservedLineSkus)
            .HasColumnName("reserved_line_skus")
            .HasMaxLength(2048)
            .IsRequired();
        builder
            .Property(s => s.ReleasedLineSkus)
            .HasColumnName("released_line_skus")
            .HasMaxLength(2048)
            .IsRequired();
        builder
            .Property(s => s.LinesAwaitingRelease)
            .HasColumnName("lines_awaiting_release")
            .IsRequired();
    }
}
