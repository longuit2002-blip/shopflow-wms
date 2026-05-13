using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using ShopFlow.Inventory.Infrastructure;

#pragma warning disable CA1707 // Identifiers should not contain underscores — EF migration class name encodes timestamp + descriptor

namespace ShopFlow.Inventory.Infrastructure.Migrations;

/// <summary>
/// Initial Inventory schema per Tech Design v3.0 §4.2 — applied per-tenant.
/// Carries both <see cref="MigrationAttribute"/> and
/// <see cref="DbContextAttribute"/> per AGENTS.md §3.23; without them
/// <c>MigrateAsync()</c> is a silent no-op (see
/// <c>docs/solutions/2026-05-10-ef-migration-needs-attributes.md</c>).
/// </summary>
/// <remarks>
/// Per ADR-0003 no business table carries <c>tenant_id</c>. The
/// idempotency anchor on <c>reservations_ledger</c> is
/// <c>UNIQUE(order_id)</c>, NOT <c>UNIQUE(tenant_id, order_id)</c>.
/// </remarks>
[DbContext(typeof(InventoryDbContext))]
[Migration("20260512000001_InitialInventorySchema")]
public sealed partial class InitialInventorySchema : Migration
{
    protected override void Up(MigrationBuilder mb)
    {
        mb.CreateTable(
            name: "stock_items",
            columns: table => new
            {
                sku = table.Column<string>(maxLength: 64, nullable: false),
                available = table.Column<int>(nullable: false),
                reserved = table.Column<int>(nullable: false),
                created_at = table.Column<DateTime>(nullable: false),
                updated_at = table.Column<DateTime>(nullable: true),
                row_version = table.Column<uint>(
                    type: "xid",
                    nullable: false,
                    defaultValueSql: "(txid_current())::text::xid"
                ),
            },
            constraints: table => table.PrimaryKey("pk_stock_items", x => x.sku)
        );

        mb.CreateTable(
            name: "reservations_ledger",
            columns: table => new
            {
                id = table.Column<Guid>(nullable: false),
                sku = table.Column<string>(maxLength: 64, nullable: false),
                order_id = table.Column<string>(maxLength: 128, nullable: false),
                quantity = table.Column<int>(nullable: false),
                status = table.Column<string>(maxLength: 16, nullable: false),
                expires_at = table.Column<DateTime>(nullable: false),
                confirmed_at = table.Column<DateTime>(nullable: true),
                released_at = table.Column<DateTime>(nullable: true),
                expired_at = table.Column<DateTime>(nullable: true),
                created_at = table.Column<DateTime>(nullable: false),
                updated_at = table.Column<DateTime>(nullable: true),
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_reservations_ledger", x => x.id);
                table.ForeignKey(
                    name: "fk_reservations_stock_items_sku",
                    column: x => x.sku,
                    principalTable: "stock_items",
                    principalColumn: "sku",
                    onDelete: ReferentialAction.Restrict
                );
            }
        );

        mb.CreateIndex(
            name: "ux_reservations_order_id",
            table: "reservations_ledger",
            column: "order_id",
            unique: true
        );

        mb.CreateIndex(
            name: "ix_reservations_status_expires_at",
            table: "reservations_ledger",
            columns: new[] { "status", "expires_at" }
        );

        mb.CreateTable(
            name: "stock_adjustments",
            columns: table => new
            {
                id = table.Column<Guid>(nullable: false),
                sku = table.Column<string>(maxLength: 64, nullable: false),
                delta = table.Column<int>(nullable: false),
                reason = table.Column<string>(maxLength: 32, nullable: false),
                note = table.Column<string>(maxLength: 512, nullable: true),
                created_at = table.Column<DateTime>(nullable: false),
                updated_at = table.Column<DateTime>(nullable: true),
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_stock_adjustments", x => x.id);
                table.ForeignKey(
                    name: "fk_stock_adjustments_stock_items_sku",
                    column: x => x.sku,
                    principalTable: "stock_items",
                    principalColumn: "sku",
                    onDelete: ReferentialAction.Restrict
                );
            }
        );

        mb.CreateIndex(
            name: "ix_stock_adjustments_sku_created_at",
            table: "stock_adjustments",
            columns: new[] { "sku", "created_at" }
        );

        // Module-prefixed outbox table per Sprint-2.5 — required because
        // Inbound + Inventory share one physical tenant DB (ADR-0003).
        // PK + index names also prefixed for symmetry. See
        // docs/solutions/2026-05-13-cross-module-outbox-table-name-collision.md.
        // Edited in-place vs the original Phase-0-redux U8 ship because no
        // production tenants have been provisioned against the prior name;
        // a rename migration is unnecessary.
        mb.CreateTable(
            name: "inventory_outbox_messages",
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
            constraints: table => table.PrimaryKey("pk_inventory_outbox_messages", x => x.id)
        );

        mb.CreateIndex(
            name: "ix_inventory_outbox_messages_pending",
            table: "inventory_outbox_messages",
            columns: new[] { "processed_at", "created_at" }
        );
    }

    protected override void Down(MigrationBuilder mb)
    {
        mb.DropTable(name: "inventory_outbox_messages");
        mb.DropTable(name: "stock_adjustments");
        mb.DropTable(name: "reservations_ledger");
        mb.DropTable(name: "stock_items");
    }
}
