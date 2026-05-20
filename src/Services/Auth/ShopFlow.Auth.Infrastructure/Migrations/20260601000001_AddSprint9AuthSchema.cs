using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using ShopFlow.Auth.Infrastructure;

#pragma warning disable CA1707 // Identifiers should not contain underscores — EF migration class name encodes timestamp + descriptor

namespace ShopFlow.Auth.Infrastructure.Migrations;

/// <summary>
/// Sprint-9 U3 — consolidated Auth schema extension. Adds 5 lockout +
/// MFA columns to <c>users</c> + creates 5 new tables + the
/// <c>auth_outbox_messages</c> outbox table. Carries both
/// <see cref="MigrationAttribute"/> + <see cref="DbContextAttribute"/>
/// per AGENTS.md §3.23 — without them <c>MigrateAsync()</c> is a silent
/// no-op (see
/// <c>docs/solutions/2026-05-10-ef-migration-needs-attributes.md</c>).
/// </summary>
/// <remarks>
/// <para>Safe to apply against any existing tenant DB: the column adds
/// carry DEFAULT values so existing rows backfill cleanly; the new
/// tables don't touch pre-existing schema.</para>
///
/// <para><c>shopflow-migrate provision</c> + <c>seed-owner</c> remain
/// the entry points for legacy-tenant retrofit; the new
/// <c>RolePermissionsSeed</c> (U12) runs right after this migration to
/// pre-populate the Owner role's full permission grant.</para>
/// </remarks>
[DbContext(typeof(AuthDbContext))]
[Migration("20260601000001_AddSprint9AuthSchema")]
public sealed partial class AddSprint9AuthSchema : Migration
{
    protected override void Up(MigrationBuilder mb)
    {
        ArgumentNullException.ThrowIfNull(mb);

        // -------- 1. Extend users with lockout + MFA columns --------
        mb.AddColumn<int>(
            name: "failed_login_count",
            table: "users",
            nullable: false,
            defaultValue: 0);

        mb.AddColumn<DateTime>(
            name: "locked_until",
            table: "users",
            nullable: true);

        mb.AddColumn<DateTime>(
            name: "last_failed_login_at",
            table: "users",
            nullable: true);

        mb.AddColumn<bool>(
            name: "mfa_required",
            table: "users",
            nullable: false,
            defaultValue: false);

        mb.AddColumn<bool>(
            name: "mfa_enrolled",
            table: "users",
            nullable: false,
            defaultValue: false);

        // -------- 2. password_reset_tokens --------
        mb.CreateTable(
            name: "password_reset_tokens",
            columns: table => new
            {
                token_hash = table.Column<byte[]>(type: "bytea", nullable: false),
                user_id = table.Column<Guid>(nullable: false),
                expires_at = table.Column<DateTime>(nullable: false),
                created_at = table.Column<DateTime>(nullable: false),
                used_at = table.Column<DateTime>(nullable: true),
            },
            constraints: table => table.PrimaryKey("pk_password_reset_tokens", x => x.token_hash));

        mb.CreateIndex(
            name: "ix_password_reset_tokens_user_created",
            table: "password_reset_tokens",
            columns: new[] { "user_id", "created_at" });

        // -------- 3. user_totp_secrets --------
        mb.CreateTable(
            name: "user_totp_secrets",
            columns: table => new
            {
                user_id = table.Column<Guid>(nullable: false),
                encrypted_secret = table.Column<byte[]>(type: "bytea", nullable: false),
                totp_key_id = table.Column<short>(type: "smallint", nullable: false),
                last_used_step = table.Column<long>(nullable: true),
                created_at = table.Column<DateTime>(nullable: false),
                updated_at = table.Column<DateTime>(nullable: true),
            },
            constraints: table => table.PrimaryKey("pk_user_totp_secrets", x => x.user_id));

        // -------- 4. user_recovery_codes --------
        mb.CreateTable(
            name: "user_recovery_codes",
            columns: table => new
            {
                user_id = table.Column<Guid>(nullable: false),
                code_hash = table.Column<string>(maxLength: 256, nullable: false),
                created_at = table.Column<DateTime>(nullable: false),
                used_at = table.Column<DateTime>(nullable: true),
            },
            constraints: table => table.PrimaryKey("pk_user_recovery_codes", x => new { x.user_id, x.code_hash }));

        // Partial index on active (unused) codes only — the consume path
        // probes for any unused code for the user, so a filtered index
        // saves work as the user accumulates consumed codes.
        mb.Sql(
            "CREATE INDEX ix_user_recovery_codes_user_active "
            + "ON user_recovery_codes (user_id) WHERE used_at IS NULL;");

        // -------- 5. role_permissions --------
        mb.CreateTable(
            name: "role_permissions",
            columns: table => new
            {
                role = table.Column<string>(maxLength: 16, nullable: false),
                permission_key = table.Column<string>(maxLength: 64, nullable: false),
                created_at = table.Column<DateTime>(nullable: false),
            },
            constraints: table => table.PrimaryKey("pk_role_permissions", x => new { x.role, x.permission_key }));

        // CHECK constraint pinned to the UserRole enum (mirrors the
        // chk_users_role pattern from Sprint-8 U3).
        mb.Sql(
            "ALTER TABLE role_permissions "
            + "ADD CONSTRAINT chk_role_permissions_role "
            + "CHECK (role IN ('Owner', 'Picker', 'Dispatcher'));");

        // -------- 6. auth_audit_log --------
        mb.CreateTable(
            name: "auth_audit_log",
            columns: table => new
            {
                id = table.Column<long>(nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", Npgsql.EntityFrameworkCore.PostgreSQL.Metadata.NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                event_type = table.Column<string>(maxLength: 64, nullable: false),
                user_id = table.Column<Guid>(nullable: true),
                source_ip = table.Column<string>(maxLength: 64, nullable: false),
                user_agent = table.Column<string>(maxLength: 512, nullable: false),
                metadata_json = table.Column<string>(type: "jsonb", nullable: false),
                correlation_id = table.Column<Guid>(nullable: false),
                occurred_at = table.Column<DateTime>(nullable: false),
            },
            constraints: table => table.PrimaryKey("pk_auth_audit_log", x => x.id));

        mb.CreateIndex(
            name: "ix_auth_audit_log_event_occurred",
            table: "auth_audit_log",
            columns: new[] { "event_type", "occurred_at" });

        mb.CreateIndex(
            name: "ix_auth_audit_log_user_occurred",
            table: "auth_audit_log",
            columns: new[] { "user_id", "occurred_at" });

        // -------- 7. auth_outbox_messages --------
        mb.CreateTable(
            name: "auth_outbox_messages",
            columns: table => new
            {
                id = table.Column<Guid>(nullable: false),
                tenant_id = table.Column<Guid>(nullable: false),
                event_type = table.Column<string>(maxLength: 256, nullable: false),
                payload = table.Column<string>(type: "jsonb", nullable: false),
                trace_id = table.Column<string>(maxLength: 64, nullable: true),
                created_at = table.Column<DateTime>(nullable: false),
                processed_at = table.Column<DateTime>(nullable: true),
                retry_count = table.Column<int>(nullable: false),
                last_error = table.Column<string>(maxLength: 2048, nullable: true),
            },
            constraints: table => table.PrimaryKey("pk_auth_outbox_messages", x => x.id));

        mb.CreateIndex(
            name: "ix_auth_outbox_messages_pending",
            table: "auth_outbox_messages",
            columns: new[] { "processed_at", "created_at" });
    }

    protected override void Down(MigrationBuilder mb)
    {
        ArgumentNullException.ThrowIfNull(mb);

        mb.DropTable(name: "auth_outbox_messages");
        mb.DropTable(name: "auth_audit_log");
        mb.Sql("ALTER TABLE role_permissions DROP CONSTRAINT IF EXISTS chk_role_permissions_role;");
        mb.DropTable(name: "role_permissions");
        mb.Sql("DROP INDEX IF EXISTS ix_user_recovery_codes_user_active;");
        mb.DropTable(name: "user_recovery_codes");
        mb.DropTable(name: "user_totp_secrets");
        mb.DropTable(name: "password_reset_tokens");

        mb.DropColumn(name: "mfa_enrolled", table: "users");
        mb.DropColumn(name: "mfa_required", table: "users");
        mb.DropColumn(name: "last_failed_login_at", table: "users");
        mb.DropColumn(name: "locked_until", table: "users");
        mb.DropColumn(name: "failed_login_count", table: "users");
    }
}
