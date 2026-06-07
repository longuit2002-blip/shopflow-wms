using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using ShopFlow.Inventory.Infrastructure;

#pragma warning disable CA1707 // EF migration class name encodes timestamp + descriptor

namespace ShopFlow.Inventory.Infrastructure.Migrations;

/// <summary>
/// Sprint-7.5 U10 — Inventory module index audit per plan KTD7.
///
/// Most Inventory hot paths are already well-indexed:
/// <list type="bullet">
///   <item>U3 added <c>ix_skus_category</c> + partial
///   <c>ix_skus_is_flash_sale</c> + partial <c>ux_skus_barcode</c></item>
///   <item>U6 added <c>ix_reservations_ledger_sku_created_at_id</c> for
///   cursor pagination</item>
///   <item>Sprint-1-redux migration covers
///   <c>ux_reservations_order_id_line</c> +
///   <c>ix_reservations_status_expires_at</c></item>
/// </list>
///
/// This audit adds two remaining gaps for big-data list/diagnostic
/// queries:
/// <list type="bullet">
///   <item><c>ix_stock_adjustments_created_at</c> — defensive timeline
///   index on the audit trail for operator-diagnostics ("recent stock
///   adjustments across all SKUs"). The existing
///   <c>ix_stock_adjustments_sku_created_at</c> covers per-SKU lookups;
///   this covers the cross-SKU time-range scan.</item>
///   <item><c>ix_stock_item_bins_bin_id</c> — defensive FK-side index
///   for the put-away suggestion service's "what's currently in this
///   bin" scan. PK on (sku, bin_id) covers the per-SKU direction; this
///   covers the inverse.</item>
/// </list>
///
/// Pure raw-SQL via <c>mb.Sql</c> + <c>CREATE INDEX IF NOT EXISTS</c>.
/// </summary>
[DbContext(typeof(InventoryDbContext))]
[Migration("20260519000009_InventoryIndexAudit")]
public sealed partial class InventoryIndexAudit : Migration
{
    protected override void Up(MigrationBuilder mb)
    {
        ArgumentNullException.ThrowIfNull(mb);
        mb.Sql(
            "CREATE INDEX IF NOT EXISTS ix_stock_adjustments_created_at "
                + "ON stock_adjustments (created_at DESC);"
        );
        mb.Sql(
            "CREATE INDEX IF NOT EXISTS ix_stock_item_bins_bin_id " + "ON stock_item_bins (bin_id);"
        );
    }

    protected override void Down(MigrationBuilder mb)
    {
        ArgumentNullException.ThrowIfNull(mb);
        mb.Sql("DROP INDEX IF EXISTS ix_stock_item_bins_bin_id;");
        mb.Sql("DROP INDEX IF EXISTS ix_stock_adjustments_created_at;");
    }
}
