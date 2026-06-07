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
///   <item><c>ix_stock_sync_push_log_channel_observed_at</c> — supports
///   the diagnostic SyncStateController's "recent pushes by channel"
///   query; ordered DESC for newest-first display.</item>
///   <item><c>ix_stock_sync_sku_flag_updated_at</c> — supports observing
///   freshly-flipped flash-sale state for sanity-check dashboards.</item>
/// </list>
///
/// <para><strong>Finish-line U3 correction.</strong> The original Sprint-7.5
/// migration referenced names that don't exist on the Sprint-5 schema, on
/// BOTH indexes: <c>(channel_id, occurred_at)</c> — the push-log table has
/// <c>channel_type</c> + <c>observed_at</c> — and table <c>sku_flags</c> —
/// the table is <c>stock_sync_sku_flag</c> (the Sprint-2.5 per-module prefix,
/// singular). It was judgment-authored without a local Docker daemon (the
/// Sprint-7.5 U10 deviation note), so it failed (`42703 column "channel_id"`,
/// then `42P01 relation "sku_flags"`) on every real migration apply — which
/// is why no StockSync integration test ever ran. Corrected to the real
/// schema names.</para>
///
/// Sprint-5 U7's <c>UNIQUE(idempotency_key)</c> on <c>stock_sync_push_log</c>
/// already covers idempotent insert. The PK on <c>stock_sync_sku_flag.sku</c>
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
            "CREATE INDEX IF NOT EXISTS ix_stock_sync_push_log_channel_observed_at "
                + "ON stock_sync_push_log (channel_type, observed_at DESC);"
        );
        mb.Sql(
            "CREATE INDEX IF NOT EXISTS ix_stock_sync_sku_flag_updated_at "
                + "ON stock_sync_sku_flag (updated_at DESC);"
        );
    }

    protected override void Down(MigrationBuilder mb)
    {
        ArgumentNullException.ThrowIfNull(mb);
        mb.Sql("DROP INDEX IF EXISTS ix_stock_sync_sku_flag_updated_at;");
        mb.Sql("DROP INDEX IF EXISTS ix_stock_sync_push_log_channel_observed_at;");
    }
}
