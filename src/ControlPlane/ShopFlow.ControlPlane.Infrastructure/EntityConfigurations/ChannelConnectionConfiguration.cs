using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ShopFlow.ControlPlane.Domain;

namespace ShopFlow.ControlPlane.Infrastructure.EntityConfigurations;

/// <summary>
/// Fluent map for the <c>channel_connections</c> table per Tech Design
/// v3.0 §1.5. <c>channel_id</c> is the primary key (the natural identifier
/// used by inbound webhooks); the BaseEntity <c>Id</c> column is shadowed
/// onto it.
/// </summary>
internal sealed class ChannelConnectionConfiguration : IEntityTypeConfiguration<ChannelConnection>
{
    public void Configure(EntityTypeBuilder<ChannelConnection> builder)
    {
        builder.ToTable("channel_connections");

        builder.HasKey(c => c.ChannelId);

        builder.Property(c => c.ChannelId).HasColumnName("channel_id").ValueGeneratedNever();

        builder.Ignore(c => c.Id);

        builder.Property(c => c.TenantId).HasColumnName("tenant_id").IsRequired();

        builder
            .Property(c => c.ChannelType)
            .HasColumnName("channel_type")
            .HasMaxLength(32)
            .IsRequired();

        builder
            .Property(c => c.SecretEncrypted)
            .HasColumnName("secret_encrypted")
            .HasColumnType("bytea")
            .IsRequired();

        builder.Property(c => c.CreatedAt).HasColumnName("created_at").IsRequired();

        builder.Property(c => c.UpdatedAt).HasColumnName("updated_at");

        builder.HasIndex(c => c.TenantId).HasDatabaseName("ix_channel_connections_tenant_id");
    }
}
