using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using ShopFlow.Inventory.Infrastructure;

#pragma warning disable CA1707 // Identifiers should not contain underscores — EF migration class name encodes timestamp + descriptor

namespace ShopFlow.Inventory.Infrastructure.Migrations;

/// <summary>
/// Sprint-3-redux U3 / K10 — extend <c>reservations_ledger</c> with an
/// <c>order_line_id</c> column so multi-line orders can share one
/// <c>order_id</c> across N rows (one row per line). The Sprint-1-redux
/// idempotency anchor moves from <c>UNIQUE(order_id)</c> to
/// <c>UNIQUE(order_id, order_line_id)</c>; single-line callers (the
/// existing <c>TryReserveAsync(sku, orderId, qty, ttl)</c> path) pass
/// <c>order_line_id='_default'</c> internally, so backwards compatibility
/// is preserved at the composite-UNIQUE level.
/// </summary>
/// <remarks>
/// Carries both <see cref="MigrationAttribute"/> and
/// <see cref="DbContextAttribute"/> per AGENTS.md §3.23 — without them
/// <c>MigrateAsync()</c> is a silent no-op (see
/// <c>docs/solutions/2026-05-10-ef-migration-needs-attributes.md</c>).
/// </remarks>
[DbContext(typeof(InventoryDbContext))]
[Migration("20260513000010_AddOrderLineIdToReservationsLedger")]
public sealed partial class AddOrderLineIdToReservationsLedger : Migration
{
    protected override void Up(MigrationBuilder mb)
    {
        // Add the new column with a default so existing Sprint-1-redux rows
        // (single-line shape) stay valid under the new composite UNIQUE.
        mb.AddColumn<string>(
            name: "order_line_id",
            table: "reservations_ledger",
            type: "text",
            nullable: false,
            defaultValue: "_default"
        );

        // Drop the old single-column UNIQUE and replace with the composite.
        mb.DropIndex(name: "ux_reservations_order_id", table: "reservations_ledger");

        mb.CreateIndex(
            name: "ux_reservations_order_id_line",
            table: "reservations_ledger",
            columns: new[] { "order_id", "order_line_id" },
            unique: true
        );
    }

    protected override void Down(MigrationBuilder mb)
    {
        mb.DropIndex(name: "ux_reservations_order_id_line", table: "reservations_ledger");

        mb.CreateIndex(
            name: "ux_reservations_order_id",
            table: "reservations_ledger",
            column: "order_id",
            unique: true
        );

        mb.DropColumn(name: "order_line_id", table: "reservations_ledger");
    }
}
