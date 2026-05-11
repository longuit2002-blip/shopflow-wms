using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ShopFlow.ControlPlane.Domain;

namespace ShopFlow.ControlPlane.Infrastructure.EntityConfigurations;

/// <summary>
/// Fluent map for the <c>tenant_events</c> audit table per Tech Design
/// v3.0 §1.5. Append-only — no <c>updated_at</c>, no domain event buffer.
/// Indexed on <c>(tenant_id, occurred_at DESC)</c> so per-tenant audit
/// queries are an index scan.
/// </summary>
internal sealed class TenantEventConfiguration : IEntityTypeConfiguration<TenantEvent>
{
    public void Configure(EntityTypeBuilder<TenantEvent> builder)
    {
        builder.ToTable("tenant_events");

        builder.HasKey(e => e.Id);

        builder.Property(e => e.Id).HasColumnName("id");

        builder.Property(e => e.TenantId).HasColumnName("tenant_id").IsRequired();

        builder
            .Property(e => e.EventType)
            .HasColumnName("event_type")
            .HasMaxLength(128)
            .IsRequired();

        builder
            .Property(e => e.PayloadJson)
            .HasColumnName("payload")
            .HasColumnType("jsonb")
            .IsRequired();

        builder.Property(e => e.OccurredAt).HasColumnName("occurred_at").IsRequired();

        builder
            .HasIndex(e => new { e.TenantId, e.OccurredAt })
            .HasDatabaseName("ix_tenant_events_tenant_occurred_at")
            .IsDescending(false, true);
    }
}
