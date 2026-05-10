using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ShopFlow.Inventory.Infrastructure.Migrations;

/// <summary>
/// Initial schema for the Inventory module: <c>stock_items</c>,
/// <c>reservations_ledger</c>, <c>stock_adjustments</c>, and
/// <c>outbox_messages</c>. Mirrors Tech Design §7.2 / §11.1 / §21.1
/// verbatim. RLS policies and the partial covering index from §7.3 are
/// applied via raw SQL because they cannot be expressed through the EF
/// migration builder.
/// </summary>
public partial class InitialInventorySchema : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // ---- stock_items ----------------------------------------------------
        migrationBuilder.CreateTable(
            name: "stock_items",
            columns: table => new
            {
                tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                sku = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false),
                id = table.Column<Guid>(type: "uuid", nullable: false),
                name = table.Column<string>(type: "varchar(256)", maxLength: 256, nullable: false),
                category = table.Column<string>(
                    type: "varchar(128)",
                    maxLength: 128,
                    nullable: true
                ),
                total_qty = table.Column<int>(type: "integer", nullable: false),
                allocated_qty = table.Column<int>(type: "integer", nullable: false),
                safety_threshold = table.Column<int>(type: "integer", nullable: false),
                row_version = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                created_at = table.Column<DateTime>(
                    type: "timestamp with time zone",
                    nullable: false
                ),
                updated_at = table.Column<DateTime>(
                    type: "timestamp with time zone",
                    nullable: true
                ),
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_stock_items", x => new { x.tenant_id, x.sku });
            }
        );

        migrationBuilder.CreateIndex(
            name: "ix_stock_items_tenant_id",
            table: "stock_items",
            columns: new[] { "tenant_id", "id" }
        );

        migrationBuilder.Sql(
            @"ALTER TABLE stock_items
                ADD CONSTRAINT ck_stock_items_total_qty_nonneg CHECK (total_qty >= 0),
                ADD CONSTRAINT ck_stock_items_allocated_qty_nonneg CHECK (allocated_qty >= 0),
                ADD CONSTRAINT ck_stock_items_safety_threshold_nonneg CHECK (safety_threshold >= 0);"
        );

        // ---- reservations_ledger -------------------------------------------
        migrationBuilder.CreateTable(
            name: "reservations_ledger",
            columns: table => new
            {
                tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                sku = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false),
                id = table.Column<Guid>(type: "uuid", nullable: false),
                qty = table.Column<int>(type: "integer", nullable: false),
                order_id = table.Column<Guid>(type: "uuid", nullable: false),
                status = table.Column<string>(type: "varchar(16)", maxLength: 16, nullable: false),
                reserved_at = table.Column<DateTime>(
                    type: "timestamp with time zone",
                    nullable: false
                ),
                expires_at = table.Column<DateTime>(
                    type: "timestamp with time zone",
                    nullable: false
                ),
                finalized_at = table.Column<DateTime>(
                    type: "timestamp with time zone",
                    nullable: true
                ),
            },
            constraints: table =>
            {
                table.PrimaryKey(
                    "pk_reservations_ledger",
                    x => new
                    {
                        x.tenant_id,
                        x.sku,
                        x.id,
                    }
                );
            }
        );

        migrationBuilder.CreateIndex(
            name: "ux_reservations_tenant_order",
            table: "reservations_ledger",
            columns: new[] { "tenant_id", "order_id" },
            unique: true
        );

        migrationBuilder.Sql(
            @"ALTER TABLE reservations_ledger
                ADD CONSTRAINT ck_reservations_qty_positive CHECK (qty > 0),
                ADD CONSTRAINT ck_reservations_status CHECK
                    (status IN ('Active', 'Confirmed', 'Released', 'Expired'));"
        );

        // Partial covering index per Tech Design §7.3 — INCLUDE not
        // expressible via the EF migration builder.
        migrationBuilder.Sql(
            @"CREATE INDEX idx_active_reservations
                ON reservations_ledger (tenant_id, sku) INCLUDE (qty)
                WHERE status = 'Active';"
        );

        // ---- stock_adjustments ---------------------------------------------
        migrationBuilder.CreateTable(
            name: "stock_adjustments",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                stock_item_id = table.Column<Guid>(type: "uuid", nullable: false),
                quantity_delta = table.Column<int>(type: "integer", nullable: false),
                reason = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false),
                user_id = table.Column<Guid>(type: "uuid", nullable: false),
                notes = table.Column<string>(
                    type: "varchar(1024)",
                    maxLength: 1024,
                    nullable: true
                ),
                created_at = table.Column<DateTime>(
                    type: "timestamp with time zone",
                    nullable: false
                ),
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_stock_adjustments", x => x.id);
            }
        );

        migrationBuilder.CreateIndex(
            name: "ix_stock_adjustments_tenant_item_created",
            table: "stock_adjustments",
            columns: new[] { "tenant_id", "stock_item_id", "created_at" }
        );

        // ---- outbox_messages ------------------------------------------------
        migrationBuilder.CreateTable(
            name: "outbox_messages",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                event_type = table.Column<string>(type: "text", nullable: false),
                payload = table.Column<string>(type: "text", nullable: false),
                trace_id = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: true),
                created_at = table.Column<DateTime>(
                    type: "timestamp with time zone",
                    nullable: false
                ),
                processed_at = table.Column<DateTime>(
                    type: "timestamp with time zone",
                    nullable: true
                ),
                retry_count = table.Column<int>(type: "integer", nullable: false),
                last_error = table.Column<string>(type: "text", nullable: true),
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_outbox_messages", x => x.id);
            }
        );

        migrationBuilder.CreateIndex(
            name: "ix_outbox_unprocessed",
            table: "outbox_messages",
            columns: new[] { "processed_at", "created_at" }
        );

        // ---- RLS policies ---------------------------------------------------
        // Per AGENTS.md §3.15 every tenant-scoped table gets an RLS policy
        // in the same migration that creates it. The application sets
        // `app.tenant_id` per connection via the kernel's TenancyInterceptor
        // + a connection-checkout hook (lands in U7); reads through that
        // connection see only the active tenant's rows even if a handler
        // forgets the WHERE clause.
        migrationBuilder.Sql(
            @"ALTER TABLE stock_items ENABLE ROW LEVEL SECURITY;
              CREATE POLICY tenant_isolation_stock_items ON stock_items
                  USING (tenant_id::text = current_setting('app.tenant_id', true));

              ALTER TABLE reservations_ledger ENABLE ROW LEVEL SECURITY;
              CREATE POLICY tenant_isolation_reservations_ledger ON reservations_ledger
                  USING (tenant_id::text = current_setting('app.tenant_id', true));

              ALTER TABLE stock_adjustments ENABLE ROW LEVEL SECURITY;
              CREATE POLICY tenant_isolation_stock_adjustments ON stock_adjustments
                  USING (tenant_id::text = current_setting('app.tenant_id', true));

              ALTER TABLE outbox_messages ENABLE ROW LEVEL SECURITY;
              CREATE POLICY tenant_isolation_outbox_messages ON outbox_messages
                  USING (tenant_id::text = current_setting('app.tenant_id', true));"
        );
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            @"DROP POLICY IF EXISTS tenant_isolation_outbox_messages ON outbox_messages;
              DROP POLICY IF EXISTS tenant_isolation_stock_adjustments ON stock_adjustments;
              DROP POLICY IF EXISTS tenant_isolation_reservations_ledger ON reservations_ledger;
              DROP POLICY IF EXISTS tenant_isolation_stock_items ON stock_items;"
        );

        migrationBuilder.Sql(@"DROP INDEX IF EXISTS idx_active_reservations;");

        migrationBuilder.DropTable(name: "outbox_messages");
        migrationBuilder.DropTable(name: "stock_adjustments");
        migrationBuilder.DropTable(name: "reservations_ledger");
        migrationBuilder.DropTable(name: "stock_items");
    }
}
