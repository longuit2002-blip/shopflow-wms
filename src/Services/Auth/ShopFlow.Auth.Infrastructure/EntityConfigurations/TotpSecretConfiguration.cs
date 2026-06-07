using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ShopFlow.Auth.Domain.Entities;

namespace ShopFlow.Auth.Infrastructure.EntityConfigurations;

/// <summary>
/// Sprint-9 U3 fluent map for <c>user_totp_secrets</c>. One row per
/// enrolled user; <see cref="TotpSecret.UserId"/> is the PK.
/// <c>totp_key_id</c> identifies which KEK encrypted the blob (KTD8
/// lazy rotation).
/// </summary>
internal sealed class TotpSecretConfiguration : IEntityTypeConfiguration<TotpSecret>
{
    public void Configure(EntityTypeBuilder<TotpSecret> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("user_totp_secrets");

        builder.HasKey(s => s.UserId).HasName("pk_user_totp_secrets");

        builder.Property(s => s.UserId).HasColumnName("user_id").IsRequired();

        builder
            .Property(s => s.EncryptedSecret)
            .HasColumnName("encrypted_secret")
            .HasColumnType("bytea")
            .IsRequired();

        builder
            .Property(s => s.TotpKeyId)
            .HasColumnName("totp_key_id")
            .HasColumnType("smallint")
            .IsRequired();

        builder.Property(s => s.LastUsedTimeStep).HasColumnName("last_used_step");

        builder.Property(s => s.CreatedAt).HasColumnName("created_at").IsRequired();

        builder.Property(s => s.UpdatedAt).HasColumnName("updated_at");
    }
}
