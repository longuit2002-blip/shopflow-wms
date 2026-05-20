using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;
using ShopFlow.StockSync.Domain.Aggregates;

namespace ShopFlow.StockSync.Infrastructure.EntityConfigurations;

/// <summary>
/// Fluent map for <c>stock_sync_push_log</c> (Sprint-5 plan U1/U5/R12). The
/// <c>ux_stock_sync_push_log_idempotency</c> UNIQUE on
/// <c>idempotency_key</c> catches MassTransit at-least-once redelivery via
/// 23505 (Sprint-1-redux <c>ReservationRepository</c> pattern).
/// </summary>
internal sealed class PushLogEntryConfiguration : IEntityTypeConfiguration<PushLogEntry>
{
    public void Configure(EntityTypeBuilder<PushLogEntry> builder)
    {
        builder.ToTable(
            "stock_sync_push_log",
            tb =>
                tb.HasCheckConstraint(
                    "ck_stock_sync_push_log_status",
                    "status IN ('Success', 'Failed', 'BreakerOpen')"
                )
        );

        builder.HasKey(p => p.Id).HasName("pk_stock_sync_push_log");

        // BIGSERIAL — Sprint-2-redux U4 carry-forward rule: use the typed
        // enum, not a plain string, otherwise Npgsql doesn't recognize the
        // identity annotation.
        builder
            .Property(p => p.Id)
            .HasColumnName("id")
            .HasAnnotation(
                "Npgsql:ValueGenerationStrategy",
                NpgsqlValueGenerationStrategy.IdentityByDefaultColumn
            );

        builder.Property(p => p.TenantId).HasColumnName("tenant_id").IsRequired();

        builder
            .Property(p => p.ChannelType)
            .HasColumnName("channel_type")
            .HasMaxLength(32)
            .IsRequired();

        builder
            .Property(p => p.Sku)
            .HasColumnName("sku")
            .HasMaxLength(64)
            .IsRequired();

        builder.Property(p => p.Available).HasColumnName("available").IsRequired();

        builder
            .Property(p => p.IdempotencyKey)
            .HasColumnName("idempotency_key")
            .HasMaxLength(128)
            .IsRequired();

        builder
            .Property(p => p.Status)
            .HasColumnName("status")
            .HasMaxLength(16)
            .IsRequired();

        builder.Property(p => p.ErrorCode).HasColumnName("error_code").HasMaxLength(64);

        builder.Property(p => p.LatencyMs).HasColumnName("latency_ms").IsRequired();

        builder.Property(p => p.ObservedAt).HasColumnName("observed_at").IsRequired();

        builder.Property(p => p.PushedAt).HasColumnName("pushed_at").IsRequired();

        builder
            .HasIndex(p => p.IdempotencyKey)
            .IsUnique()
            .HasDatabaseName("ux_stock_sync_push_log_idempotency");

        builder
            .HasIndex(p => new { p.TenantId, p.ChannelType, p.PushedAt })
            .HasDatabaseName("ix_stock_sync_push_log_tenant_channel_pushed");
    }
}
