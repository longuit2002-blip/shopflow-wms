using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;
using ShopFlow.Inventory.Infrastructure;

#pragma warning disable CA1707 // Identifiers should not contain underscores — EF migration class name encodes timestamp + descriptor

namespace ShopFlow.Inventory.Infrastructure.Migrations;

/// <summary>
/// Sprint-2-redux U4 — extends the Inventory schema with the four new
/// tables required for bin-aware put-away (R13) + the
/// <c>inbound_dedup</c> idempotency anchor (R11) + the nullable
/// <c>home_zone_id</c> FK on <c>stock_items</c> for put-away ranking
/// (R16).
/// </summary>
[DbContext(typeof(InventoryDbContext))]
[Migration("20260513000001_AddBinsAndZonesAndInboundDedup")]
public sealed partial class AddBinsAndZonesAndInboundDedup : Migration
{
    protected override void Up(MigrationBuilder mb)
    {
        mb.CreateTable(
            name: "zones",
            columns: table => new
            {
                zone_id = table
                    .Column<long>(nullable: false)
                    .Annotation(
                        "Npgsql:ValueGenerationStrategy",
                        NpgsqlValueGenerationStrategy.IdentityByDefaultColumn
                    ),
                name = table.Column<string>(maxLength: 64, nullable: false),
                warehouse_id = table.Column<string>(maxLength: 64, nullable: false),
            },
            constraints: table => table.PrimaryKey("pk_zones", x => x.zone_id)
        );

        mb.CreateTable(
            name: "bins",
            columns: table => new
            {
                bin_id = table
                    .Column<long>(nullable: false)
                    .Annotation(
                        "Npgsql:ValueGenerationStrategy",
                        NpgsqlValueGenerationStrategy.IdentityByDefaultColumn
                    ),
                zone_id = table.Column<long>(nullable: false),
                name = table.Column<string>(maxLength: 64, nullable: false),
                capacity = table.Column<int>(nullable: false),
                occupancy_qty = table.Column<int>(nullable: false),
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_bins", x => x.bin_id);
                table.ForeignKey(
                    name: "fk_bins_zones",
                    column: x => x.zone_id,
                    principalTable: "zones",
                    principalColumn: "zone_id",
                    onDelete: ReferentialAction.Restrict
                );
            }
        );

        mb.CreateIndex(name: "ix_bins_zone_id", table: "bins", column: "zone_id");

        mb.AddColumn<long>(name: "home_zone_id", table: "stock_items", nullable: true);

        mb.AddForeignKey(
            name: "fk_stock_items_zones",
            table: "stock_items",
            column: "home_zone_id",
            principalTable: "zones",
            principalColumn: "zone_id",
            onDelete: ReferentialAction.SetNull
        );

        mb.CreateTable(
            name: "stock_item_bins",
            columns: table => new
            {
                sku = table.Column<string>(maxLength: 64, nullable: false),
                bin_id = table.Column<long>(nullable: false),
                quantity = table.Column<int>(nullable: false),
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_stock_item_bins", x => new { x.sku, x.bin_id });
                table.ForeignKey(
                    name: "fk_stock_item_bins_stock_items",
                    column: x => x.sku,
                    principalTable: "stock_items",
                    principalColumn: "sku",
                    onDelete: ReferentialAction.Restrict
                );
                table.ForeignKey(
                    name: "fk_stock_item_bins_bins",
                    column: x => x.bin_id,
                    principalTable: "bins",
                    principalColumn: "bin_id",
                    onDelete: ReferentialAction.Restrict
                );
            }
        );

        mb.CreateTable(
            name: "inbound_dedup",
            columns: table => new
            {
                receiving_id = table.Column<Guid>(nullable: false),
                line_id = table.Column<Guid>(nullable: false),
                sku = table.Column<string>(maxLength: 64, nullable: false),
                quantity = table.Column<int>(nullable: false),
                processed_at = table.Column<DateTime>(nullable: false),
            },
            constraints: table =>
                table.PrimaryKey("pk_inbound_dedup", x => new { x.receiving_id, x.line_id })
        );
    }

    protected override void Down(MigrationBuilder mb)
    {
        mb.DropTable(name: "inbound_dedup");
        mb.DropTable(name: "stock_item_bins");
        mb.DropForeignKey(name: "fk_stock_items_zones", table: "stock_items");
        mb.DropColumn(name: "home_zone_id", table: "stock_items");
        mb.DropTable(name: "bins");
        mb.DropTable(name: "zones");
    }
}
