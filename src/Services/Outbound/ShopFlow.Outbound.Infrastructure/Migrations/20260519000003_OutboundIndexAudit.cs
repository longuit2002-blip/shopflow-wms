using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using ShopFlow.Outbound.Infrastructure;

#pragma warning disable CA1707 // EF migration class name encodes timestamp + descriptor

namespace ShopFlow.Outbound.Infrastructure.Migrations;

/// <summary>
/// Sprint-7.5 U10 — Outbound module index audit per plan KTD7.
///
/// Identifies indexes the existing Sprint-3-redux + Sprint-7 schema
/// migrations omitted but that the Outbound API's hot-path queries
/// (<c>ListOrdersHandler</c>, <c>GetOrderDetailHandler</c>,
/// <c>GetOrderTransitionsHandler</c>) depend on for big-data
/// (post-Sprint-7.6 seed) performance.
///
/// Adds:
/// <list type="bullet">
///   <item><c>ix_outbound_orders_status_created_at</c> — supports
///   <c>ListOrdersHandler</c>'s typical filter (by saga state /
///   status) + DESC newest-first ordering.</item>
///   <item><c>ix_outbound_order_lines_order_id</c> — defensive index
///   for the LineItems join path; PK + FK may already cover, idempotent
///   CREATE INDEX IF NOT EXISTS keeps it safe regardless.</item>
/// </list>
///
/// Pure raw-SQL via <c>mb.Sql</c> + <c>CREATE INDEX IF NOT EXISTS</c>
/// so the migration is no-op if the index happens to already exist
/// (mirrors Sprint-3-redux convention for cross-cutting index work).
/// </summary>
[DbContext(typeof(OutboundDbContext))]
[Migration("20260519000003_OutboundIndexAudit")]
public sealed partial class OutboundIndexAudit : Migration
{
    protected override void Up(MigrationBuilder mb)
    {
        ArgumentNullException.ThrowIfNull(mb);
        mb.Sql(
            "CREATE INDEX IF NOT EXISTS ix_outbound_orders_status_created_at "
            + "ON outbound_orders (status, created_at DESC);");
        mb.Sql(
            "CREATE INDEX IF NOT EXISTS ix_outbound_order_lines_order_id "
            + "ON outbound_order_lines (order_id);");
    }

    protected override void Down(MigrationBuilder mb)
    {
        ArgumentNullException.ThrowIfNull(mb);
        mb.Sql("DROP INDEX IF EXISTS ix_outbound_order_lines_order_id;");
        mb.Sql("DROP INDEX IF EXISTS ix_outbound_orders_status_created_at;");
    }
}
