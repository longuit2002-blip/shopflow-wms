using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using ShopFlow.ControlPlane.Infrastructure;

#pragma warning disable CA1707 // Identifiers should not contain underscores — EF migration class name encodes timestamp + descriptor

namespace ShopFlow.ControlPlane.Migrations;

/// <summary>
/// Initial schema for <c>shopflow_control</c> per Tech Design v3.0 §1.5:
/// <c>tenants</c> + <c>tenant_events</c> + <c>channel_connections</c>.
/// Hand-authored migration class carrying both <see cref="MigrationAttribute"/>
/// and <see cref="DbContextAttribute"/> per AGENTS.md §3.23 (without these,
/// <c>MigrateAsync()</c> is a silent no-op — see
/// <c>docs/solutions/2026-05-10-ef-migration-needs-attributes.md</c>).
/// </summary>
[DbContext(typeof(ControlPlaneDbContext))]
[Migration("20260512000000_InitialControlPlaneSchema")]
public sealed partial class InitialControlPlaneSchema : Migration
{
    protected override void Up(MigrationBuilder mb)
    {
        mb.CreateTable(
            name: "tenants",
            columns: table => new
            {
                id = table.Column<Guid>(nullable: false),
                slug = table.Column<string>(maxLength: 64, nullable: false),
                db_name = table.Column<string>(maxLength: 128, nullable: false),
                region = table.Column<string>(maxLength: 32, nullable: false),
                tier = table.Column<string>(maxLength: 32, nullable: false),
                status = table.Column<string>(maxLength: 32, nullable: false),
                business_reg = table.Column<string>(maxLength: 128, nullable: true),
                sub_processors = table.Column<string>(type: "jsonb", nullable: false),
                created_at = table.Column<DateTime>(nullable: false),
                updated_at = table.Column<DateTime>(nullable: true),
                provisioned_at = table.Column<DateTime>(nullable: true),
                archiving_at = table.Column<DateTime>(nullable: true),
                archived_at = table.Column<DateTime>(nullable: true),
                breach_notified_at = table.Column<DateTime>(nullable: true),
                last_failure_reason = table.Column<string>(maxLength: 2048, nullable: true),
                row_version = table.Column<uint>(
                    type: "xid",
                    nullable: false,
                    defaultValueSql: "(txid_current())::text::xid"
                ),
            },
            constraints: table => table.PrimaryKey("pk_tenants", x => x.id)
        );

        mb.CreateIndex(name: "ux_tenants_slug", table: "tenants", column: "slug", unique: true);

        mb.CreateIndex(
            name: "ux_tenants_db_name",
            table: "tenants",
            column: "db_name",
            unique: true
        );

        mb.CreateTable(
            name: "tenant_events",
            columns: table => new
            {
                id = table.Column<Guid>(nullable: false),
                tenant_id = table.Column<Guid>(nullable: false),
                event_type = table.Column<string>(maxLength: 128, nullable: false),
                payload = table.Column<string>(type: "jsonb", nullable: false),
                occurred_at = table.Column<DateTime>(nullable: false),
            },
            constraints: table => table.PrimaryKey("pk_tenant_events", x => x.id)
        );

        mb.CreateIndex(
            name: "ix_tenant_events_tenant_occurred_at",
            table: "tenant_events",
            columns: new[] { "tenant_id", "occurred_at" },
            descending: new[] { false, true }
        );

        mb.CreateTable(
            name: "channel_connections",
            columns: table => new
            {
                channel_id = table.Column<Guid>(nullable: false),
                tenant_id = table.Column<Guid>(nullable: false),
                channel_type = table.Column<string>(maxLength: 32, nullable: false),
                secret_encrypted = table.Column<byte[]>(type: "bytea", nullable: false),
                created_at = table.Column<DateTime>(nullable: false),
                updated_at = table.Column<DateTime>(nullable: true),
            },
            constraints: table => table.PrimaryKey("pk_channel_connections", x => x.channel_id)
        );

        mb.CreateIndex(
            name: "ix_channel_connections_tenant_id",
            table: "channel_connections",
            column: "tenant_id"
        );
    }

    protected override void Down(MigrationBuilder mb)
    {
        mb.DropTable(name: "channel_connections");
        mb.DropTable(name: "tenant_events");
        mb.DropTable(name: "tenants");
    }
}
