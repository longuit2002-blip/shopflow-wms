using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using ShopFlow.Inbound.Infrastructure;

#pragma warning disable CA1707 // EF migration class name encodes timestamp + descriptor

namespace ShopFlow.Inbound.Infrastructure.Migrations;

/// <summary>
/// Sprint-7.5 U10 — Inbound module index audit per plan KTD7.
///
/// Adds defensive composite indexes for the Inbound list-page query
/// patterns the Sprint-2-redux schema didn't anticipate:
/// <list type="bullet">
///   <item><c>ix_purchase_orders_status_created_at</c> — supports PO
///   list filter (by state) + DESC newest-first ordering.</item>
///   <item><c>ix_receivings_purchase_order_id</c> — defensive index on
///   the FK join path. PK + FK may already cover.</item>
/// </list>
///
/// Pure raw-SQL via <c>mb.Sql</c> + <c>CREATE INDEX IF NOT EXISTS</c>.
/// </summary>
[DbContext(typeof(InboundDbContext))]
[Migration("20260519000005_InboundIndexAudit")]
public sealed partial class InboundIndexAudit : Migration
{
    protected override void Up(MigrationBuilder mb)
    {
        ArgumentNullException.ThrowIfNull(mb);
        mb.Sql(
            "CREATE INDEX IF NOT EXISTS ix_purchase_orders_status_created_at "
            + "ON purchase_orders (status, created_at DESC);");
        mb.Sql(
            "CREATE INDEX IF NOT EXISTS ix_receivings_purchase_order_id "
            + "ON receivings (purchase_order_id);");
    }

    protected override void Down(MigrationBuilder mb)
    {
        ArgumentNullException.ThrowIfNull(mb);
        mb.Sql("DROP INDEX IF EXISTS ix_receivings_purchase_order_id;");
        mb.Sql("DROP INDEX IF EXISTS ix_purchase_orders_status_created_at;");
    }
}
