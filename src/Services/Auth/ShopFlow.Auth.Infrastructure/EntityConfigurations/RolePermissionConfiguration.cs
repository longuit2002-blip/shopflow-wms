using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ShopFlow.Auth.Domain;
using ShopFlow.Auth.Domain.Entities;

namespace ShopFlow.Auth.Infrastructure.EntityConfigurations;

/// <summary>
/// Sprint-9 U3 fluent map for <c>role_permissions</c>. Composite PK
/// <c>(role, permission_key)</c>. Role stored as enum name string so
/// the DB row is grep-friendly and survives enum reordering.
/// </summary>
internal sealed class RolePermissionConfiguration : IEntityTypeConfiguration<RolePermission>
{
    public void Configure(EntityTypeBuilder<RolePermission> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("role_permissions");

        builder
            .HasKey(rp => new { rp.Role, rp.PermissionKey })
            .HasName("pk_role_permissions");

        builder
            .Property(rp => rp.Role)
            .HasColumnName("role")
            .HasMaxLength(16)
            .HasConversion(
                v => v.ToString(),
                v => Enum.Parse<UserRole>(v))
            .IsRequired();

        builder
            .Property(rp => rp.PermissionKey)
            .HasColumnName("permission_key")
            .HasMaxLength(64)
            .IsRequired();

        builder
            .Property(rp => rp.CreatedAt)
            .HasColumnName("created_at")
            .IsRequired();
    }
}
