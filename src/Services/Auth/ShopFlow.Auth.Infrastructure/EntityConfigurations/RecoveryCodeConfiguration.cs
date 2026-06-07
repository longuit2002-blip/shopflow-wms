using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ShopFlow.Auth.Domain.Entities;

namespace ShopFlow.Auth.Infrastructure.EntityConfigurations;

/// <summary>
/// Sprint-9 U3 fluent map for <c>user_recovery_codes</c>. Composite PK
/// <c>(user_id, code_hash)</c>; <see cref="RecoveryCode.CodeHash"/> is
/// the Argon2id-RecoveryCode-profile PHC string (up to ~120 chars).
/// </summary>
internal sealed class RecoveryCodeConfiguration : IEntityTypeConfiguration<RecoveryCode>
{
    public void Configure(EntityTypeBuilder<RecoveryCode> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("user_recovery_codes");

        builder.HasKey(c => new { c.UserId, c.CodeHash }).HasName("pk_user_recovery_codes");

        builder.Property(c => c.UserId).HasColumnName("user_id").IsRequired();

        builder.Property(c => c.CodeHash).HasColumnName("code_hash").HasMaxLength(256).IsRequired();

        builder.Property(c => c.CreatedAt).HasColumnName("created_at").IsRequired();

        builder.Property(c => c.UsedAt).HasColumnName("used_at");

        builder
            .HasIndex(c => c.UserId)
            .HasFilter("used_at IS NULL")
            .HasDatabaseName("ix_user_recovery_codes_user_active");
    }
}
