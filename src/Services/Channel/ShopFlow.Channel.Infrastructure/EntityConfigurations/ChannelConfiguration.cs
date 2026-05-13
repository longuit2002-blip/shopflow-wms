using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ChannelAggregate = ShopFlow.Channel.Domain.Channels.Channel;

namespace ShopFlow.Channel.Infrastructure.EntityConfigurations;

/// <summary>
/// Fluent map for <c>channels</c> per Sprint-4 plan U2. PK <c>id</c> mirrors
/// the control-plane <c>ChannelConnection.ChannelId</c> so the receiver's
/// directory lookup resolves to this row. No FK across DBs — the
/// tenant-side row is a denormalized projection per Tech Design v3.0 §6.
/// </summary>
internal sealed class ChannelConfiguration : IEntityTypeConfiguration<ChannelAggregate>
{
    public void Configure(EntityTypeBuilder<ChannelAggregate> builder)
    {
        builder.ToTable("channels");

        builder.Ignore(c => c.DomainEvents);

        builder.HasKey(c => c.Id).HasName("pk_channels");
        builder.Property(c => c.Id).HasColumnName("id");

        builder
            .Property(c => c.ChannelType)
            .HasColumnName("channel_type")
            .HasMaxLength(32)
            .IsRequired();

        builder
            .Property(c => c.Status)
            .HasColumnName("status")
            .HasMaxLength(16)
            .HasConversion<string>()
            .IsRequired();

        builder.Property(c => c.DisabledAt).HasColumnName("disabled_at");
        builder.Property(c => c.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(c => c.UpdatedAt).HasColumnName("updated_at");
    }
}
