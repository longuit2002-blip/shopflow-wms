using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ShopFlow.Auth.Domain.Entities;

namespace ShopFlow.Auth.Infrastructure.EntityConfigurations;

/// <summary>
/// Sprint-9 U3 fluent map for <c>password_reset_tokens</c>. The PK is
/// the token hash itself — the table is logical-OLAP-grade tiny (one
/// row per outstanding request per user, TTL 30 min) and the hash
/// uniquely identifies the request.
/// </summary>
internal sealed class PasswordResetTokenConfiguration : IEntityTypeConfiguration<PasswordResetToken>
{
    public void Configure(EntityTypeBuilder<PasswordResetToken> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("password_reset_tokens");

        builder.HasKey(t => t.TokenHash).HasName("pk_password_reset_tokens");

        builder
            .Property(t => t.TokenHash)
            .HasColumnName("token_hash")
            .HasColumnType("bytea")
            .IsRequired();

        builder.Property(t => t.UserId).HasColumnName("user_id").IsRequired();

        builder.Property(t => t.ExpiresAt).HasColumnName("expires_at").IsRequired();

        builder.Property(t => t.CreatedAt).HasColumnName("created_at").IsRequired();

        builder.Property(t => t.UsedAt).HasColumnName("used_at");

        builder
            .HasIndex(t => new { t.UserId, t.CreatedAt })
            .HasDatabaseName("ix_password_reset_tokens_user_created");
    }
}
