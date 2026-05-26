using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using ShopFlow.Inventory.Infrastructure;

#pragma warning disable CA1707 // Identifiers should not contain underscores — EF migration class name encodes timestamp + descriptor

namespace ShopFlow.Inventory.Infrastructure.Migrations;

/// <summary>
/// Sprint-7.5 U3 — replace the in-memory <c>InMemorySkuMetadataStore</c>
/// singleton with a real per-tenant <c>skus</c> catalog table. Closes
/// Sprint-6 trade-off #1 (cosmetic SKU schema expansion) by promoting
/// the 10 metadata columns out of process memory and into the
/// per-tenant DB.
/// </summary>
/// <remarks>
/// <para>Carries both <see cref="MigrationAttribute"/> and
/// <see cref="DbContextAttribute"/> per AGENTS.md §3.23 — without them
/// <c>MigrateAsync()</c> is a silent no-op (see
/// <c>docs/solutions/2026-05-10-ef-migration-needs-attributes.md</c>).</para>
///
/// <para><strong>Index strategy (R2 / KTD2).</strong>
/// Three production indexes:
/// </para>
/// <list type="bullet">
///   <item><description><c>ix_skus_category</c> — btree on <c>category</c>
///   so the Inventory screen's category filter does not table-scan once
///   the catalog grows past a few thousand rows.</description></item>
///   <item><description><c>ix_skus_is_flash_sale</c> — partial btree
///   <c>WHERE is_flash_sale = TRUE</c>. StockSync's hot path queries
///   "is this SKU flash-sale?"; the partial form keeps the index small
///   (only a handful of rows are flash-sale at any moment) and the
///   non-matching rows touch zero index pages.</description></item>
///   <item><description><c>ux_skus_barcode</c> — partial UNIQUE
///   <c>WHERE barcode IS NOT NULL</c>. Some SKUs ship without a printed
///   barcode; the partial form lets multiple NULL rows coexist while
///   still rejecting duplicate scanned barcodes with a Postgres 23505.
///   </description></item>
/// </list>
/// </remarks>
[DbContext(typeof(InventoryDbContext))]
[Migration("20260519000001_AddSkusRichCatalog")]
public sealed partial class AddSkusRichCatalog : Migration
{
    protected override void Up(MigrationBuilder mb)
    {
        mb.CreateTable(
            name: "skus",
            columns: table => new
            {
                sku = table.Column<string>(maxLength: 64, nullable: false),
                name = table.Column<string>(maxLength: 256, nullable: false),
                category = table.Column<string>(maxLength: 128, nullable: true),
                threshold = table.Column<int>(nullable: true),
                weight_grams = table.Column<int>(nullable: true),
                dimensions = table.Column<string>(type: "jsonb", nullable: true),
                description = table.Column<string>(maxLength: 2048, nullable: true),
                image_url = table.Column<string>(maxLength: 1024, nullable: true),
                barcode = table.Column<string>(maxLength: 64, nullable: true),
                brand = table.Column<string>(maxLength: 128, nullable: true),
                is_flash_sale = table.Column<bool>(nullable: false, defaultValue: false),
                created_at = table.Column<DateTime>(nullable: false),
                updated_at = table.Column<DateTime>(nullable: true),
            },
            constraints: table => table.PrimaryKey("pk_skus", x => x.sku)
        );

        // Btree on category — supports the Inventory screen's category
        // filter once catalogs grow past a few thousand rows.
        mb.CreateIndex(name: "ix_skus_category", table: "skus", column: "category");

        // Partial btree on is_flash_sale = TRUE — StockSync hot-path
        // bypass-check stays cheap, non-matching rows touch zero pages.
        mb.CreateIndex(
            name: "ix_skus_is_flash_sale",
            table: "skus",
            column: "is_flash_sale",
            filter: "\"is_flash_sale\" = TRUE"
        );

        // Partial UNIQUE on barcode WHERE NOT NULL — multiple NULL
        // barcodes coexist; duplicate non-null barcodes raise 23505.
        mb.CreateIndex(
            name: "ux_skus_barcode",
            table: "skus",
            column: "barcode",
            unique: true,
            filter: "\"barcode\" IS NOT NULL"
        );
    }

    protected override void Down(MigrationBuilder mb)
    {
        mb.DropTable(name: "skus");
    }
}
