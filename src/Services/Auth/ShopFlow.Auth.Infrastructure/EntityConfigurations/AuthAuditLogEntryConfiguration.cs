using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ShopFlow.Auth.Domain.Entities;

namespace ShopFlow.Auth.Infrastructure.EntityConfigurations;

/// <summary>
/// Sprint-9 U3 fluent map for <c>auth_audit_log</c>. <c>id</c> is
/// <c>bigserial</c> (Postgres identity); EF treats it as a generated
/// value.
/// </summary>
internal sealed class AuthAuditLogEntryConfiguration : IEntityTypeConfiguration<AuthAuditLogEntry>
{
    public void Configure(EntityTypeBuilder<AuthAuditLogEntry> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("auth_audit_log");

        builder.HasKey(e => e.Id).HasName("pk_auth_audit_log");

        builder
            .Property(e => e.Id)
            .HasColumnName("id")
            .UseIdentityByDefaultColumn();

        builder
            .Property(e => e.EventType)
            .HasColumnName("event_type")
            .HasMaxLength(64)
            .IsRequired();

        builder
            .Property(e => e.UserId)
            .HasColumnName("user_id");

        builder
            .Property(e => e.SourceIp)
            .HasColumnName("source_ip")
            .HasMaxLength(64)
            .IsRequired();

        builder
            .Property(e => e.UserAgent)
            .HasColumnName("user_agent")
            .HasMaxLength(512)
            .IsRequired();

        builder
            .Property(e => e.MetadataJson)
            .HasColumnName("metadata_json")
            .HasColumnType("jsonb")
            .IsRequired();

        builder
            .Property(e => e.CorrelationId)
            .HasColumnName("correlation_id")
            .IsRequired();

        builder
            .Property(e => e.OccurredAt)
            .HasColumnName("occurred_at")
            .IsRequired();

        builder
            .HasIndex(e => new { e.EventType, e.OccurredAt })
            .HasDatabaseName("ix_auth_audit_log_event_occurred");

        builder
            .HasIndex(e => new { e.UserId, e.OccurredAt })
            .HasDatabaseName("ix_auth_audit_log_user_occurred");
    }
}
