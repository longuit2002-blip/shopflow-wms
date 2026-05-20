using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ShopFlow.Auth.Domain;
using ShopFlow.Auth.Domain.Entities;

namespace ShopFlow.Auth.Infrastructure.EntityConfigurations;

/// <summary>
/// Fluent map for the Sprint-8 U3 <c>users</c> table — the per-tenant
/// User aggregate.
/// </summary>
/// <remarks>
/// <para>Column shape:</para>
/// <list type="bullet">
///   <item><description><c>id uuid PRIMARY KEY</c> — the aggregate's Guid id.</description></item>
///   <item><description><c>email varchar(254) NOT NULL</c> — lower-cased
///   by the factory; the UNIQUE index is on <c>lower(email)</c>
///   defined via raw SQL in the migration since EF can't express
///   expression-indexes through the fluent API.</description></item>
///   <item><description><c>password_hash text NOT NULL</c> — PHC modular
///   string (e.g. <c>$argon2id$v=19$m=65536,t=4,p=4$&lt;salt&gt;$&lt;hash&gt;</c>).
///   <c>text</c> rather than a fixed-length column because the PHC
///   length depends on Argon2 parameters that may change across
///   future tunings (Sprint-9+).</description></item>
///   <item><description><c>role varchar(16) NOT NULL</c> — stored as the
///   enum name string (Owner / Picker / Dispatcher). DB-level CHECK
///   constraint enforces the same set, pinned to <c>UserRoleTests</c>
///   in U1.</description></item>
///   <item><description><c>is_active boolean NOT NULL DEFAULT true</c>
///   — soft-delete flag.</description></item>
///   <item><description><c>last_login_at timestamptz NULL</c> — set by
///   <c>RecordLogin</c>; null for users who have never signed in.</description></item>
///   <item><description><c>created_at timestamptz NOT NULL</c> + <c>updated_at timestamptz NULL</c>
///   — inherited from BaseEntity.</description></item>
/// </list>
///
/// <para>The aggregate's <c>DomainEvents</c> buffer is ignored — events
/// are drained by the OutboxInterceptor at SaveChanges time, never
/// persisted with the row.</para>
/// </remarks>
internal sealed class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("users");

        builder.Ignore(u => u.DomainEvents);

        builder.HasKey(u => u.Id).HasName("pk_users");

        builder
            .Property(u => u.Id)
            .HasColumnName("id")
            .IsRequired();

        builder
            .Property(u => u.Email)
            .HasColumnName("email")
            .HasMaxLength(254)
            .IsRequired();

        builder
            .Property(u => u.PasswordHash)
            .HasColumnName("password_hash")
            .HasColumnType("text")
            .IsRequired();

        builder
            .Property(u => u.Role)
            .HasColumnName("role")
            .HasMaxLength(16)
            .HasConversion(
                v => v.ToString(),
                v => Enum.Parse<UserRole>(v)
            )
            .IsRequired();

        builder
            .Property(u => u.IsActive)
            .HasColumnName("is_active")
            .HasDefaultValue(true)
            .IsRequired();

        builder
            .Property(u => u.LastLoginAt)
            .HasColumnName("last_login_at");

        builder
            .Property(u => u.CreatedAt)
            .HasColumnName("created_at")
            .IsRequired();

        builder
            .Property(u => u.UpdatedAt)
            .HasColumnName("updated_at");

        // ux_users_email_lower (UNIQUE on lower(email)) + chk_users_role
        // CHECK constraint are emitted by the AddUsers migration via raw
        // SQL — EF's fluent API can't express expression-based UNIQUE
        // indexes or table-level CHECK constraints declaratively.
    }
}
