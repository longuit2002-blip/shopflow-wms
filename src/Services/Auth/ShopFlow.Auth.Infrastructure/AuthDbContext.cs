using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using ShopFlow.Auth.Domain.Entities;
using ShopFlow.Auth.Infrastructure.EntityConfigurations;

namespace ShopFlow.Auth.Infrastructure;

/// <summary>
/// EF Core context for one tenant's Auth schema. Constructed per request
/// from the AddAuthModule lambda bound to
/// <c>IRequestContext.DbConnectionString</c> (AGENTS.md §3.17). Direct
/// <c>new AuthDbContext(...)</c> outside a registration lambda /
/// *Factory / *Tests / *Fixture type is forbidden by ShopFlow0003.
/// </summary>
/// <remarks>
/// <para>Schema per Sprint-8 U3 — one table:</para>
/// <list type="bullet">
///   <item><description><c>users</c> — per-tenant User aggregate.
///   Case-insensitive UNIQUE on <c>lower(email)</c> via
///   <c>ux_users_email_lower</c>; DB-level CHECK constraint
///   <c>chk_users_role</c> mirrors the <c>UserRole</c> enum.</description></item>
/// </list>
/// <para>Per ADR-0003 no business table carries <c>tenant_id</c>; the
/// database identity IS the tenant boundary. Refresh tokens live in
/// Redis (Sprint-8 U5), not here — per-tenant DB connection storms on
/// every refresh call are sidestepped by Redis namespacing
/// (<c>refresh:{tenantSlug}:{userId}:{tokenHash}</c>).</para>
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
    /// <c>AddUsersMigrationSmokeTests</c>. See
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

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);
        base.OnModelCreating(modelBuilder);

        modelBuilder.ApplyConfiguration(new UserConfiguration());
    }
}
