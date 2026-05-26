using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using ShopFlow.StockSync.Infrastructure;

#pragma warning disable CA1707 // EF migration class name encodes timestamp + descriptor

namespace ShopFlow.StockSync.Infrastructure.Migrations;

/// <summary>
/// Sprint-7.5 U10 — StockSync module index audit per plan KTD7.
///
/// Adds defensive composite indexes for the StockSync push-log query
/// patterns the Sprint-5 schema didn't anticipate:
/// <list type="bullet">
///   <item><c>ix_stock_sync_push_log_channel_occurred_at</c> — supports
///   the diagnostic SyncStateController's "recent pushes by channel"
///   query; ordered DESC for newest-first display.</item>
///   <item><c>ix_sku_flags_updated_at</c> — supports observing freshly-
///   flipped flash-sale state for sanity-check dashboards.</item>
/// </list>
///
/// Sprint-5 U7's <c>UNIQUE(idempotency_key)</c> on <c>stock_sync_push_log</c>
/// already covers idempotent insert. The PK on <c>sku_flags.sku</c>
/// already covers the dispatch hot-path read. These additions target
/// operator-observability queries, not the hot path.
///
/// Pure raw-SQL via <c>mb.Sql</c> + <c>CREATE INDEX IF NOT EXISTS</c>.
/// </summary>
[DbContext(typeof(StockSyncDbContext))]
[Migration("20260519000006_StockSyncIndexAudit")]
public sealed partial class StockSyncIndexAudit : Migration
{
    protected override void Up(MigrationBuilder mb)
    {
        ArgumentNullException.ThrowIfNull(mb);
        mb.Sql(
            "CREATE INDEX IF NOT EXISTS ix_stock_sync_push_log_channel_occurred_at "
                + "ON stock_sync_push_log (channel_id, occurred_at DESC);"
        );
        mb.Sql(
            "CREATE INDEX IF NOT EXISTS ix_sku_flags_updated_at "
                + "ON sku_flags (updated_at DESC);"
        );
    }

    protected override void Down(MigrationBuilder mb)
    {
        ArgumentNullException.ThrowIfNull(mb);
        mb.Sql("DROP INDEX IF EXISTS ix_sku_flags_updated_at;");
        mb.Sql("DROP INDEX IF EXISTS ix_stock_sync_push_log_channel_occurred_at;");
    }
}
