using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using ShopFlow.Channel.Infrastructure;

#pragma warning disable CA1707 // EF migration class name encodes timestamp + descriptor

namespace ShopFlow.Channel.Infrastructure.Migrations;

/// <summary>
/// Sprint-7.5 U10 — Channel module index audit per plan KTD7.
///
/// Adds defensive indexes for webhook ingest paths under big-data
/// volumes:
/// <list type="bullet">
///   <item><c>ix_webhook_events_received_at</c> — Sprint-4's
///   <c>UNIQUE(channel_id, provider_event_id)</c> covers idempotent
///   lookups; this composite supports time-range queries operators
///   run for "what arrived in the last hour" diagnostics.</item>
///   <item><c>ix_product_mappings_channel_id_external_sku</c> —
///   defensive composite for the Channel adapter's resolution step
///   (Sprint-4 product mapping fast-path). PK / UNIQUE may already
///   cover; IF NOT EXISTS keeps it safe.</item>
/// </list>
///
/// Pure raw-SQL via <c>mb.Sql</c> + <c>CREATE INDEX IF NOT EXISTS</c>.
/// </summary>
[DbContext(typeof(ChannelDbContext))]
[Migration("20260519000004_ChannelIndexAudit")]
public sealed partial class ChannelIndexAudit : Migration
{
    protected override void Up(MigrationBuilder mb)
    {
        ArgumentNullException.ThrowIfNull(mb);
        mb.Sql(
            "CREATE INDEX IF NOT EXISTS ix_webhook_events_received_at "
            + "ON webhook_events (received_at DESC);");
        mb.Sql(
            "CREATE INDEX IF NOT EXISTS ix_product_mappings_channel_id_external_sku "
            + "ON product_mappings (channel_id, external_sku);");
    }

    protected override void Down(MigrationBuilder mb)
    {
        ArgumentNullException.ThrowIfNull(mb);
        mb.Sql("DROP INDEX IF EXISTS ix_product_mappings_channel_id_external_sku;");
        mb.Sql("DROP INDEX IF EXISTS ix_webhook_events_received_at;");
    }
}
