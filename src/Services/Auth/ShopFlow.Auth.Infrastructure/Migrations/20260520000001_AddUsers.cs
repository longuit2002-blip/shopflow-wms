using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using ShopFlow.Auth.Infrastructure;

#pragma warning disable CA1707 // Identifiers should not contain underscores — EF migration class name encodes timestamp + descriptor

namespace ShopFlow.Auth.Infrastructure.Migrations;

/// <summary>
/// Initial Auth schema per Sprint-8 U3 — applied per-tenant by
/// <c>shopflow-migrate apply</c> on every existing + future tenant DB.
/// Carries both <see cref="MigrationAttribute"/> and
/// <see cref="DbContextAttribute"/> per AGENTS.md §3.23; without them
/// <c>MigrateAsync()</c> is a silent no-op (see
/// <c>docs/solutions/2026-05-10-ef-migration-needs-attributes.md</c>).
/// </summary>
/// <remarks>
/// <para>Three additions beyond the EF-fluent table shape, all via raw
/// SQL because the fluent API does not express them:</para>
/// <list type="bullet">
///   <item><description><c>ux_users_email_lower</c> — UNIQUE index on
///   <c>lower(email)</c>. The User aggregate normalises to lowercase at
///   <c>Create</c> time so writes do not need to rewrite the column;
///   the expression-index just makes <c>WHERE lower(email) = ?</c>
///   lookups in <c>UserRepository.GetByEmailAsync</c> use an index
///   instead of a seq-scan.</description></item>
///   <item><description><c>chk_users_role</c> — CHECK constraint
///   pinning <c>role</c> to <c>('Owner', 'Picker', 'Dispatcher')</c>.
///   Mirrors the C# <see cref="ShopFlow.Auth.Domain.UserRole"/> enum;
///   <c>UserRoleTests.cs</c> in U1 locks the agreement so any future
///   enum addition forces a coordinated migration update.</description></item>
///   <item><description><c>ix_users_role_active</c> — partial index on
///   <c>(role, is_active)</c> for the Owner-only admin "list active
///   pickers" surface in U8 (and equivalent role-filtered surfaces
///   future RBAC features will lean on). Cheap to ship now, expensive
///   to retrofit once tenants have meaningful row counts.</description></item>
/// </list>
///
/// <para>Per ADR-0003 no <c>tenant_id</c> column on the table; the
/// database identity IS the tenant boundary. The migration is safe to
/// apply against any existing tenant DB — creates a brand-new table
/// without touching any pre-existing schema. <c>shopflow-migrate
/// seed-owner</c> (Sprint-8 U10) is the companion that inserts the
/// first-Owner row after the migration runs on legacy tenants.</para>
/// </remarks>
[DbContext(typeof(AuthDbContext))]
[Migration("20260520000001_AddUsers")]
public sealed partial class AddUsers : Migration
{
    protected override void Up(MigrationBuilder mb)
    {
        ArgumentNullException.ThrowIfNull(mb);

        mb.CreateTable(
            name: "users",
            columns: table => new
            {
                id = table.Column<Guid>(nullable: false),
                email = table.Column<string>(maxLength: 254, nullable: false),
                password_hash = table.Column<string>(type: "text", nullable: false),
                role = table.Column<string>(maxLength: 16, nullable: false),
                is_active = table.Column<bool>(nullable: false, defaultValue: true),
                last_login_at = table.Column<DateTime>(nullable: true),
                created_at = table.Column<DateTime>(nullable: false),
                updated_at = table.Column<DateTime>(nullable: true),
            },
            constraints: table => table.PrimaryKey("pk_users", x => x.id)
        );

        // UNIQUE index on lower(email) — case-insensitive uniqueness.
        // Postgres supports expression indexes; EF's fluent API does
        // not, so we drop to raw SQL.
        mb.Sql(
            "CREATE UNIQUE INDEX ux_users_email_lower "
            + "ON users (lower(email));"
        );

        // CHECK constraint pinned to the UserRole enum string values.
        // Adding a 4th role in Sprint-9+ requires extending this
        // constraint via a follow-on migration AND extending the C#
        // enum + updating downstream consumers of UserRoleChangedEvent.
        mb.Sql(
            "ALTER TABLE users "
            + "ADD CONSTRAINT chk_users_role "
            + "CHECK (role IN ('Owner', 'Picker', 'Dispatcher'));"
        );

        // Partial index supporting "list active users of role R" — the
        // Owner-only admin surface in U8 and likely successors.
        mb.Sql(
            "CREATE INDEX ix_users_role_active "
            + "ON users (role) WHERE is_active = TRUE;"
        );
    }

    protected override void Down(MigrationBuilder mb)
    {
        ArgumentNullException.ThrowIfNull(mb);
        mb.Sql("DROP INDEX IF EXISTS ix_users_role_active;");
        mb.Sql("ALTER TABLE users DROP CONSTRAINT IF EXISTS chk_users_role;");
        mb.Sql("DROP INDEX IF EXISTS ux_users_email_lower;");
        mb.DropTable(name: "users");
    }
}
