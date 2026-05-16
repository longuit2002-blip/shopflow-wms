using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#pragma warning disable CA1707 // Identifiers should not contain underscores — EF migration class name encodes timestamp + descriptor

namespace ShopFlow.StockSync.Infrastructure.Migrations;

/// <summary>
/// Initial StockSync schema per Sprint-5 plan U1 — applied per-tenant.
/// Carries both <see cref="MigrationAttribute"/> and
/// <see cref="DbContextAttribute"/> per AGENTS.md §3.23; without them
/// <c>MigrateAsync()</c> is a silent no-op (see
/// <c>docs/solutions/2026-05-10-ef-migration-needs-attributes.md</c>).
/// </summary>
/// <remarks>
/// Per ADR-0003 no business table carries <c>tenant_id</c>. The push-log
/// idempotency anchor is <c>UNIQUE(idempotency_key)</c> — the dispatcher's
/// deterministic key collides on MassTransit redelivery, the repository
/// catches 23505 and returns the existing row (Sprint-1-redux pattern).
/// </remarks>
[DbContext(typeof(StockSyncDbContext))]
[Migration("20260516000001_InitialStockSyncSchema")]
public sealed partial class InitialStockSyncSchema : Migration
{
    protected override void Up(MigrationBuilder mb)
    {
        // ---- stock_sync_sku_flag (priority queue routing input) ------------
        mb.CreateTable(
            name: "stock_sync_sku_flag",
            columns: table => new
            {
                sku = table.Column<string>(maxLength: 64, nullable: false),
                is_flash_sale = table.Column<bool>(nullable: false),
                created_at = table.Column<DateTime>(nullable: false),
                updated_at = table.Column<DateTime>(nullable: true),
            },
            constraints: table => table.PrimaryKey("pk_stock_sync_sku_flag", x => x.sku)
        );

        // ---- stock_sync_push_log (audit + idempotency anchor) --------------
        mb.CreateTable(
            name: "stock_sync_push_log",
            columns: table => new
            {
                id = table
                    .Column<long>(nullable: false)
                    .Annotation(
                        "Npgsql:ValueGenerationStrategy",
                        NpgsqlValueGenerationStrategy.IdentityByDefaultColumn
                    ),
                tenant_id = table.Column<Guid>(nullable: false),
                channel_type = table.Column<string>(maxLength: 32, nullable: false),
                sku = table.Column<string>(maxLength: 64, nullable: false),
                available = table.Column<int>(nullable: false),
                idempotency_key = table.Column<string>(maxLength: 128, nullable: false),
                status = table.Column<string>(maxLength: 16, nullable: false),
                error_code = table.Column<string>(maxLength: 64, nullable: true),
                latency_ms = table.Column<int>(nullable: false),
                observed_at = table.Column<DateTime>(nullable: false),
                pushed_at = table.Column<DateTime>(nullable: false),
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_stock_sync_push_log", x => x.id);
                table.CheckConstraint(
                    "ck_stock_sync_push_log_status",
                    "status IN ('Success', 'Failed', 'BreakerOpen')"
                );
            }
        );

        mb.CreateIndex(
            name: "ux_stock_sync_push_log_idempotency",
            table: "stock_sync_push_log",
            columns: new[] { "idempotency_key" },
            unique: true
        );

        mb.CreateIndex(
            name: "ix_stock_sync_push_log_tenant_channel_pushed",
            table: "stock_sync_push_log",
            columns: new[] { "tenant_id", "channel_type", "pushed_at" }
        );

        // ---- stock_sync_outbox_messages (per-module prefix per Sprint-2.5) -
        mb.CreateTable(
            name: "stock_sync_outbox_messages",
            columns: table => new
            {
                id = table.Column<Guid>(nullable: false),
                tenant_id = table.Column<Guid>(nullable: false),
                event_type = table.Column<string>(maxLength: 256, nullable: false),
                payload = table.Column<string>(type: "jsonb", nullable: false),
                trace_id = table.Column<string>(maxLength: 64, nullable: true),
                created_at = table.Column<DateTime>(nullable: false),
                processed_at = table.Column<DateTime>(nullable: true),
                retry_count = table.Column<int>(nullable: false),
                last_error = table.Column<string>(maxLength: 2048, nullable: true),
            },
            constraints: table => table.PrimaryKey("pk_stock_sync_outbox_messages", x => x.id)
        );

        mb.CreateIndex(
            name: "ix_stock_sync_outbox_messages_pending",
            table: "stock_sync_outbox_messages",
            columns: new[] { "processed_at", "created_at" }
        );
    }

    protected override void Down(MigrationBuilder mb)
    {
        mb.DropTable(name: "stock_sync_outbox_messages");
        mb.DropTable(name: "stock_sync_push_log");
        mb.DropTable(name: "stock_sync_sku_flag");
    }
}
