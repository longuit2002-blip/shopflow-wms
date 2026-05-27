using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using ShopFlow.Auth.Infrastructure;

#pragma warning disable CA1707 // Identifiers should not contain underscores — EF migration class name encodes timestamp + descriptor

namespace ShopFlow.Auth.Infrastructure.Migrations;

/// <summary>
/// Sprint-13 U1 (K1, K2, K9) — widens both Auth CHECK constraints
/// (<c>chk_users_role</c> on <c>users</c> + <c>chk_role_permissions_role</c>
/// on <c>role_permissions</c>) to include the new <c>'Packer'</c> enum
/// value. Carries both <see cref="MigrationAttribute"/> +
/// <see cref="DbContextAttribute"/> per AGENTS.md §3.23 — without them
/// <c>MigrateAsync()</c> is a silent no-op (see
/// <c>docs/solutions/2026-05-10-ef-migration-needs-attributes.md</c>).
/// </summary>
/// <remarks>
/// <para>Single migration alters BOTH constraints per Sprint-13 K2.
/// Sprint-9's <see cref="AddSprint9AuthSchema"/> added
/// <c>chk_role_permissions_role</c> mirroring Sprint-8's
/// <see cref="AddUsers"/> <c>chk_users_role</c>; widening only one
/// leaves a latent inconsistency where Packer rows in <c>users</c>
/// could not have matching <c>role_permissions</c> rows.</para>
///
/// <para>DROP-then-ADD pattern (idempotent via <c>DROP CONSTRAINT IF
/// EXISTS</c>). Safe on any tenant DB regardless of prior CHECK state.
/// No rows are inserted by this migration — <c>shopflow-migrate
/// provision</c>'s <c>RolePermissionsSeed</c> (Sprint-13 U2) is the
/// canonical entry point for writing the <c>'Packer'</c> baseline rows
/// against legacy tenants.</para>
///
/// <para>Down() reverts to the Sprint-9 3-value set
/// (<c>'Owner', 'Picker', 'Dispatcher'</c>). Any <c>'Packer'</c> rows
/// inserted post-Sprint-13 must be removed BEFORE invoking Down or the
/// re-ADDed CHECK will reject them; rollback is an operator-runbook
/// step, not an automatic data scrub.</para>
/// </remarks>
[DbContext(typeof(AuthDbContext))]
[Migration("20260602000001_AddPackerRole")]
public sealed partial class AddPackerRole : Migration
{
    protected override void Up(MigrationBuilder mb)
    {
        ArgumentNullException.ThrowIfNull(mb);

        // -------- chk_users_role: widen to 4-value set --------
        mb.Sql("ALTER TABLE users DROP CONSTRAINT IF EXISTS chk_users_role;");
        mb.Sql(
            "ALTER TABLE users "
                + "ADD CONSTRAINT chk_users_role "
                + "CHECK (role IN ('Owner', 'Picker', 'Dispatcher', 'Packer'));"
        );

        // -------- chk_role_permissions_role: widen to 4-value set --------
        mb.Sql("ALTER TABLE role_permissions DROP CONSTRAINT IF EXISTS chk_role_permissions_role;");
        mb.Sql(
            "ALTER TABLE role_permissions "
                + "ADD CONSTRAINT chk_role_permissions_role "
                + "CHECK (role IN ('Owner', 'Picker', 'Dispatcher', 'Packer'));"
        );
    }

    protected override void Down(MigrationBuilder mb)
    {
        ArgumentNullException.ThrowIfNull(mb);

        // Revert to Sprint-9 3-value set. Any 'Packer' rows must be
        // removed by an operator before this migration is rolled back.
        mb.Sql("ALTER TABLE role_permissions DROP CONSTRAINT IF EXISTS chk_role_permissions_role;");
        mb.Sql(
            "ALTER TABLE role_permissions "
                + "ADD CONSTRAINT chk_role_permissions_role "
                + "CHECK (role IN ('Owner', 'Picker', 'Dispatcher'));"
        );

        mb.Sql("ALTER TABLE users DROP CONSTRAINT IF EXISTS chk_users_role;");
        mb.Sql(
            "ALTER TABLE users "
                + "ADD CONSTRAINT chk_users_role "
                + "CHECK (role IN ('Owner', 'Picker', 'Dispatcher'));"
        );
    }
}
