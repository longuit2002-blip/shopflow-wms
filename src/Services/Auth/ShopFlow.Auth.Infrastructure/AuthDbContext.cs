using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using ShopFlow.Auth.Domain.Entities;
using ShopFlow.Auth.Infrastructure.EntityConfigurations;
using ShopFlow.SharedKernel.Infrastructure;

namespace ShopFlow.Auth.Infrastructure;

/// <summary>
/// EF Core context for one tenant's Auth schema. Constructed per request
/// from the AddAuthModule lambda bound to
/// <c>IRequestContext.DbConnectionString</c> (AGENTS.md §3.17). Direct
/// <c>new AuthDbContext(...)</c> outside a registration lambda /
/// *Factory / *Tests / *Fixture type is forbidden by ShopFlow0003.
/// </summary>
/// <remarks>
/// <para>Sprint-8 U3 schema: <c>users</c>. Sprint-9 U3 extends with:</para>
/// <list type="bullet">
///   <item><description>5 new columns on <c>users</c>: <c>failed_login_count</c>,
///   <c>locked_until</c>, <c>last_failed_login_at</c>, <c>mfa_required</c>,
///   <c>mfa_enrolled</c>.</description></item>
///   <item><description><c>password_reset_tokens</c> — outstanding reset
///   requests, PK on the SHA-256 token hash.</description></item>
///   <item><description><c>user_totp_secrets</c> — AES-256-GCM encrypted
///   TOTP secrets, PK on <c>user_id</c>.</description></item>
///   <item><description><c>user_recovery_codes</c> — Argon2-hashed
///   recovery codes, composite PK <c>(user_id, code_hash)</c>.</description></item>
///   <item><description><c>role_permissions</c> — per-role RBAC grants,
///   composite PK <c>(role, permission_key)</c>.</description></item>
///   <item><description><c>auth_audit_log</c> — append-only audit row;
///   bigserial PK.</description></item>
///   <item><description><c>auth_outbox_messages</c> — per-module
///   prefixed outbox table (Sprint-2.5 convention).</description></item>
/// </list>
/// <para>Per ADR-0003 no business table carries <c>tenant_id</c>; the
/// database identity IS the tenant boundary. Refresh tokens still live in
/// Redis, not here.</para>
/// </remarks>
public sealed class AuthDbContext : DbContext
{
    public AuthDbContext(DbContextOptions<AuthDbContext> options)
        : base(options) { }

    /// <summary>
    /// Suppress EF Core 9's
    /// <see cref="RelationalEventId.PendingModelChangesWarning"/>. Hand-authored
    /// migrations (AGENTS.md §3.23) do not ship the
    /// <c>AuthDbContextModelSnapshot.cs</c> companion that
    /// <c>dotnet ef migrations add</c> emits; without it EF compares the
    /// runtime model against an empty baseline and surfaces the entire
    /// model as "pending changes". Schema correctness is enforced by
    /// the migration smoke tests. See
    /// <c>docs/solutions/2026-05-13-ef9-pendingmodelchangeswarning-with-hand-authored-migrations.md</c>.
    /// </summary>
    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        base.OnConfiguring(optionsBuilder);
        optionsBuilder.ConfigureWarnings(w =>
            w.Ignore(RelationalEventId.PendingModelChangesWarning)
        );
    }

    public DbSet<User> Users => Set<User>();

    public DbSet<PasswordResetToken> PasswordResetTokens => Set<PasswordResetToken>();

    public DbSet<TotpSecret> TotpSecrets => Set<TotpSecret>();

    public DbSet<RecoveryCode> RecoveryCodes => Set<RecoveryCode>();

    public DbSet<RolePermission> RolePermissions => Set<RolePermission>();

    public DbSet<AuthAuditLogEntry> AuthAuditLog => Set<AuthAuditLogEntry>();

    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);
        base.OnModelCreating(modelBuilder);

        modelBuilder.ApplyConfiguration(new UserConfiguration());
        modelBuilder.ApplyConfiguration(new PasswordResetTokenConfiguration());
        modelBuilder.ApplyConfiguration(new TotpSecretConfiguration());
        modelBuilder.ApplyConfiguration(new RecoveryCodeConfiguration());
        modelBuilder.ApplyConfiguration(new RolePermissionConfiguration());
        modelBuilder.ApplyConfiguration(new AuthAuditLogEntryConfiguration());
        modelBuilder.ApplyConfiguration(new AuthOutboxMessageConfiguration());
    }
}
