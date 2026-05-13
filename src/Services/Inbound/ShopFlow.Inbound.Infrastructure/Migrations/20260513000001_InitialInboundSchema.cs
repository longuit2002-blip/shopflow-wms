using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using ShopFlow.Inbound.Infrastructure;

#pragma warning disable CA1707 // Identifiers should not contain underscores — EF migration class name encodes timestamp + descriptor

namespace ShopFlow.Inbound.Infrastructure.Migrations;

/// <summary>
/// Initial Inbound schema per Sprint-2-redux plan R3 + R5 + R9 — applied
/// per-tenant. Carries both <see cref="MigrationAttribute"/> and
/// <see cref="DbContextAttribute"/> per AGENTS.md §3.23; without them
/// <c>MigrateAsync()</c> is a silent no-op (see
/// <c>docs/solutions/2026-05-10-ef-migration-needs-attributes.md</c>).
/// </summary>
/// <remarks>
/// Per ADR-0003 no business table carries <c>tenant_id</c>. The idempotency
/// anchor on <c>receiving_lines</c> is <c>UNIQUE(receiving_id,
/// purchase_order_line_id)</c> per plan R6 — composite key catches duplicate
/// confirmation attempts at the index level.
/// </remarks>
[DbContext(typeof(InboundDbContext))]
[Migration("20260513000001_InitialInboundSchema")]
public sealed partial class InitialInboundSchema : Migration
{
    protected override void Up(MigrationBuilder mb)
    {
        mb.CreateTable(
            name: "purchase_orders",
            columns: table => new
            {
                id = table.Column<Guid>(nullable: false),
                supplier_ref = table.Column<string>(maxLength: 128, nullable: false),
                expected_delivery_at = table.Column<DateTime>(nullable: false),
                status = table.Column<string>(maxLength: 24, nullable: false),
                opened_at = table.Column<DateTime>(nullable: true),
                closed_at = table.Column<DateTime>(nullable: true),
                cancelled_at = table.Column<DateTime>(nullable: true),
                cancellation_reason = table.Column<string>(maxLength: 512, nullable: true),
                created_at = table.Column<DateTime>(nullable: false),
                updated_at = table.Column<DateTime>(nullable: true),
            },
            constraints: table => table.PrimaryKey("pk_purchase_orders", x => x.id)
        );

        mb.CreateIndex(
            name: "ix_purchase_orders_status",
            table: "purchase_orders",
            column: "status"
        );

        mb.CreateTable(
            name: "purchase_order_lines",
            columns: table => new
            {
                id = table.Column<Guid>(nullable: false),
                purchase_order_id = table.Column<Guid>(nullable: false),
                sku = table.Column<string>(maxLength: 64, nullable: false),
                expected_qty = table.Column<int>(nullable: false),
                received_qty = table.Column<int>(nullable: false),
                created_at = table.Column<DateTime>(nullable: false),
                updated_at = table.Column<DateTime>(nullable: true),
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_purchase_order_lines", x => x.id);
                table.ForeignKey(
                    name: "fk_po_lines_purchase_orders",
                    column: x => x.purchase_order_id,
                    principalTable: "purchase_orders",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict
                );
            }
        );

        mb.CreateIndex(
            name: "ix_po_lines_po_id_sku",
            table: "purchase_order_lines",
            columns: new[] { "purchase_order_id", "sku" }
        );

        mb.CreateTable(
            name: "receivings",
            columns: table => new
            {
                id = table.Column<Guid>(nullable: false),
                purchase_order_id = table.Column<Guid>(nullable: false),
                operator_id = table.Column<Guid>(nullable: true),
                occurred_at = table.Column<DateTime>(nullable: false),
                created_at = table.Column<DateTime>(nullable: false),
                updated_at = table.Column<DateTime>(nullable: true),
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_receivings", x => x.id);
                table.ForeignKey(
                    name: "fk_receivings_purchase_orders",
                    column: x => x.purchase_order_id,
                    principalTable: "purchase_orders",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict
                );
            }
        );

        mb.CreateIndex(
            name: "ix_receivings_purchase_order_id",
            table: "receivings",
            column: "purchase_order_id"
        );

        mb.CreateTable(
            name: "receiving_lines",
            columns: table => new
            {
                id = table.Column<Guid>(nullable: false),
                receiving_id = table.Column<Guid>(nullable: false),
                purchase_order_line_id = table.Column<Guid>(nullable: false),
                actual_qty = table.Column<int>(nullable: false),
                suggested_bin_id = table.Column<long>(nullable: false),
                actual_bin_id = table.Column<long>(nullable: false),
                created_at = table.Column<DateTime>(nullable: false),
                updated_at = table.Column<DateTime>(nullable: true),
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_receiving_lines", x => x.id);
                table.ForeignKey(
                    name: "fk_receiving_lines_receivings",
                    column: x => x.receiving_id,
                    principalTable: "receivings",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade
                );
            }
        );

        mb.CreateIndex(
            name: "ux_receiving_lines_receiving_line",
            table: "receiving_lines",
            columns: new[] { "receiving_id", "purchase_order_line_id" },
            unique: true
        );

        mb.CreateTable(
            name: "reconciliation_tickets",
            columns: table => new
            {
                id = table.Column<Guid>(nullable: false),
                purchase_order_id = table.Column<Guid>(nullable: false),
                purchase_order_line_id = table.Column<Guid>(nullable: false),
                receiving_id = table.Column<Guid>(nullable: false),
                sku = table.Column<string>(maxLength: 64, nullable: false),
                expected_qty = table.Column<int>(nullable: false),
                actual_qty = table.Column<int>(nullable: false),
                status = table.Column<string>(maxLength: 16, nullable: false),
                occurred_at = table.Column<DateTime>(nullable: false),
                created_at = table.Column<DateTime>(nullable: false),
                updated_at = table.Column<DateTime>(nullable: true),
            },
            constraints: table => table.PrimaryKey("pk_reconciliation_tickets", x => x.id)
        );

        mb.CreateIndex(
            name: "ix_reconciliation_tickets_status_occurred_at",
            table: "reconciliation_tickets",
            columns: new[] { "status", "occurred_at" }
        );

        mb.CreateTable(
            name: "outbox_messages",
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
            constraints: table => table.PrimaryKey("pk_outbox_messages", x => x.id)
        );

        mb.CreateIndex(
            name: "ix_outbox_messages_pending",
            table: "outbox_messages",
            columns: new[] { "processed_at", "created_at" }
        );
    }

    protected override void Down(MigrationBuilder mb)
    {
        mb.DropTable(name: "outbox_messages");
        mb.DropTable(name: "reconciliation_tickets");
        mb.DropTable(name: "receiving_lines");
        mb.DropTable(name: "receivings");
        mb.DropTable(name: "purchase_order_lines");
        mb.DropTable(name: "purchase_orders");
    }
}
