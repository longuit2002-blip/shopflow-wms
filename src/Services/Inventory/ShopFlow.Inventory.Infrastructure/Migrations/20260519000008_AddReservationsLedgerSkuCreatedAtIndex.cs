using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using ShopFlow.Inventory.Infrastructure;

#pragma warning disable CA1707 // EF migration class name encodes timestamp + descriptor

namespace ShopFlow.Inventory.Infrastructure.Migrations;

/// <summary>
/// Sprint-7.5 U6 — composite btree index on
/// <c>reservations_ledger (sku, created_at DESC, id DESC)</c> backing
/// the new opaque-cursor pagination handler. The plan called for
/// <c>(sku, occurred_at DESC)</c> but the table has no <c>occurred_at</c>
/// column (event time is computed in C# via
/// <c>ConfirmedAt ?? ReleasedAt ?? ExpiredAt ?? CreatedAt</c>);
/// <c>created_at</c> is the order key the existing handler already used
/// since Sprint-6, so the cursor + index align around it. Document
/// deviation per plan KTD4 prose.
///
/// Sub-200ms first-page render against the per-tenant million-row
/// seed (origin AE4) depends on this index — without it the cursor
/// query falls back to a seq-scan + sort.
/// </summary>
[DbContext(typeof(InventoryDbContext))]
[Migration("20260519000008_AddReservationsLedgerSkuCreatedAtIndex")]
public sealed partial class AddReservationsLedgerSkuCreatedAtIndex : Migration
{
    protected override void Up(MigrationBuilder mb)
    {
        ArgumentNullException.ThrowIfNull(mb);
        // The id column is the tie-breaker when two ledger rows share a
        // created_at instant (concurrent inserts in the same millisecond).
        // The composite (sku, created_at, id) DESC supports the cursor
        // predicate (created_at, id) < (cursor.OccurredAt, cursor.Id)
        // directly via row-value comparison.
        mb.Sql(
            "CREATE INDEX ix_reservations_ledger_sku_created_at_id "
            + "ON reservations_ledger (sku, created_at DESC, id DESC);");
    }

    protected override void Down(MigrationBuilder mb)
    {
        ArgumentNullException.ThrowIfNull(mb);
        mb.Sql("DROP INDEX IF EXISTS ix_reservations_ledger_sku_created_at_id;");
    }
}
