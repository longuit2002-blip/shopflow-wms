using Microsoft.EntityFrameworkCore;
using Npgsql;
using ShopFlow.Channel.Infrastructure;
using ShopFlow.ControlPlane.Infrastructure;
using ShopFlow.Inbound.Infrastructure;
using ShopFlow.Inventory.Infrastructure;
using ShopFlow.Outbound.Infrastructure;
using ShopFlow.StockSync.Infrastructure;

namespace ShopFlow.SharedKernel.IntegrationTests;

/// <summary>
/// Guards against the v2.0 silent-migration-no-op defect (see
/// <c>docs/solutions/2026-05-10-ef-migration-needs-attributes.md</c>) by
/// exercising <c>MigrateAsync()</c> against a fresh Testcontainers
/// Postgres for every registered DbContext. The load-bearing assertion
/// per Phase-0-redux D3:
/// <list type="number">
///   <item><description><c>__ef_migrations_history</c> row count ≥ 1 — the migration was actually applied (not silently skipped).</description></item>
///   <item><description>Each module's named tables exist.</description></item>
///   <item><description>Each module's named primary-key constraints exist.</description></item>
///   <item><description>Each module's named UNIQUE indexes exist (the idempotency anchor for Inventory's <c>order_id</c>).</description></item>
/// </list>
/// </summary>
[Collection(PostgresCollection.Name)]
[Trait("Category", "Integration")]
public sealed class MigrationSmokeTests
{
    private readonly PostgresFixture _postgres;

    public MigrationSmokeTests(PostgresFixture postgres)
    {
        _postgres = postgres;
    }

    [Fact]
    public async Task ControlPlaneMigration_AppliesAndLeavesNamedObjects()
    {
        var dbName = "smoke_control_" + Guid.NewGuid().ToString("N")[..8];
        var connStr = await _postgres.CreateDatabaseAsync(dbName);

        var options = new DbContextOptionsBuilder<ControlPlaneDbContext>()
            .UseNpgsql(connStr, npg => npg.MigrationsAssembly("ShopFlow.ControlPlane.Migrations"))
            .Options;

        await using (var ctx = new ControlPlaneDbContext(options))
        {
            await ctx.Database.MigrateAsync();
        }

        await AssertHistoryAppliedAsync(connStr);
        await AssertTablesExistAsync(
            connStr,
            new[] { "tenants", "tenant_events", "channel_connections" }
        );
        await AssertConstraintsExistAsync(
            connStr,
            new[] { "pk_tenants", "pk_tenant_events", "pk_channel_connections" }
        );
        await AssertIndexesExistAsync(connStr, new[] { "ux_tenants_slug", "ux_tenants_db_name" });
    }

    [Fact]
    public async Task InboundMigration_AppliesAndLeavesNamedObjects()
    {
        var dbName = "smoke_inbound_" + Guid.NewGuid().ToString("N")[..8];
        var connStr = await _postgres.CreateDatabaseAsync(dbName);

        var options = new DbContextOptionsBuilder<InboundDbContext>()
            .UseNpgsql(connStr, npg => npg.MigrationsAssembly("ShopFlow.Inbound.Infrastructure"))
            .Options;

        await using (var ctx = new InboundDbContext(options))
        {
            await ctx.Database.MigrateAsync();
        }

        await AssertHistoryAppliedAsync(connStr);
        await AssertTablesExistAsync(
            connStr,
            new[]
            {
                "purchase_orders",
                "purchase_order_lines",
                "receivings",
                "receiving_lines",
                "reconciliation_tickets",
                "inbound_outbox_messages",
            }
        );
        await AssertConstraintsExistAsync(
            connStr,
            new[]
            {
                "pk_purchase_orders",
                "pk_purchase_order_lines",
                "pk_receivings",
                "pk_receiving_lines",
                "pk_reconciliation_tickets",
                "pk_inbound_outbox_messages",
                "fk_po_lines_purchase_orders",
                "fk_receivings_purchase_orders",
                "fk_receiving_lines_receivings",
            }
        );
        await AssertIndexesExistAsync(
            connStr,
            new[]
            {
                "ux_receiving_lines_receiving_line",
                "ix_reconciliation_tickets_status_occurred_at",
                "ix_purchase_orders_status",
            }
        );
    }

    [Fact]
    public async Task OutboundMigration_AppliesAndLeavesNamedObjects()
    {
        var dbName = "smoke_outbound_" + Guid.NewGuid().ToString("N")[..8];
        var connStr = await _postgres.CreateDatabaseAsync(dbName);

        var options = new DbContextOptionsBuilder<OutboundDbContext>()
            .UseNpgsql(connStr, npg => npg.MigrationsAssembly("ShopFlow.Outbound.Infrastructure"))
            .Options;

        await using (var ctx = new OutboundDbContext(options))
        {
            await ctx.Database.MigrateAsync();
        }

        await AssertHistoryAppliedAsync(connStr);
        await AssertTablesExistAsync(
            connStr,
            new[]
            {
                "orders",
                "order_lines",
                "pick_waves",
                "pick_assignments",
                "pickers",
                "saga_state",
                "outbound_outbox_messages",
            }
        );
        await AssertConstraintsExistAsync(
            connStr,
            new[]
            {
                "pk_orders",
                "pk_order_lines",
                "pk_pick_waves",
                "pk_pick_assignments",
                "pk_pickers",
                "pk_saga_state",
                "pk_outbound_outbox_messages",
                "fk_order_lines_orders",
                "fk_pick_assignments_pick_waves",
                "fk_pick_assignments_orders",
            }
        );
        await AssertIndexesExistAsync(
            connStr,
            new[]
            {
                "ux_orders_channel_external_order_id",
                "ix_orders_status",
                "ix_order_lines_order_id_sku",
                "ix_pick_assignments_order_id",
                "ix_outbound_outbox_messages_pending",
            }
        );

        // saga_state column names are quoted PascalCase deliberately so
        // MassTransit's out-of-the-box EF saga repository (U4) binds to them
        // without per-column configuration. PostgreSQL preserves case for
        // quoted identifiers, so information_schema.columns reflects the
        // exact PascalCase strings used in the CREATE TABLE.
        await AssertColumnExistsAsync(connStr, "saga_state", "CorrelationId");
        await AssertColumnExistsAsync(connStr, "saga_state", "CurrentState");
        await AssertColumnExistsAsync(connStr, "saga_state", "RowVersion");
        await AssertColumnExistsAsync(connStr, "saga_state", "UpdatedAt");
    }

    [Fact]
    public async Task InventoryMigration_AppliesAndLeavesNamedObjects()
    {
        var dbName = "smoke_inventory_" + Guid.NewGuid().ToString("N")[..8];
        var connStr = await _postgres.CreateDatabaseAsync(dbName);

        var options = new DbContextOptionsBuilder<InventoryDbContext>()
            .UseNpgsql(connStr, npg => npg.MigrationsAssembly("ShopFlow.Inventory.Infrastructure"))
            .Options;

        await using (var ctx = new InventoryDbContext(options))
        {
            await ctx.Database.MigrateAsync();
        }

        await AssertHistoryAppliedAsync(connStr);
        await AssertTablesExistAsync(
            connStr,
            new[]
            {
                "stock_items",
                "reservations_ledger",
                "stock_adjustments",
                "inventory_outbox_messages",
                "zones",
                "bins",
                "stock_item_bins",
                "inbound_dedup",
            }
        );
        await AssertConstraintsExistAsync(
            connStr,
            new[]
            {
                "pk_stock_items",
                "pk_reservations_ledger",
                "pk_stock_adjustments",
                "pk_inventory_outbox_messages",
                "fk_reservations_stock_items_sku",
                "fk_stock_adjustments_stock_items_sku",
                "pk_zones",
                "pk_bins",
                "fk_bins_zones",
                "pk_stock_item_bins",
                "fk_stock_item_bins_stock_items",
                "fk_stock_item_bins_bins",
                "pk_inbound_dedup",
                "fk_stock_items_zones",
            }
        );
        // Sprint-3-redux U3/K10: idempotency anchor moved from
        // UNIQUE(order_id) to UNIQUE(order_id, order_line_id). The old
        // index is dropped; the composite one is the load-bearing assertion.
        await AssertIndexesExistAsync(
            connStr,
            new[]
            {
                "ux_reservations_order_id_line",
                "ix_reservations_status_expires_at",
                "ix_bins_zone_id",
            }
        );
        await AssertColumnExistsAsync(connStr, "stock_items", "home_zone_id");
        await AssertColumnExistsAsync(connStr, "reservations_ledger", "order_line_id");
    }

    [Fact]
    public async Task ChannelMigration_AppliesAndLeavesNamedObjects()
    {
        var dbName = "smoke_channel_" + Guid.NewGuid().ToString("N")[..8];
        var connStr = await _postgres.CreateDatabaseAsync(dbName);

        var options = new DbContextOptionsBuilder<ChannelDbContext>()
            .UseNpgsql(connStr, npg => npg.MigrationsAssembly("ShopFlow.Channel.Infrastructure"))
            .Options;

        await using (var ctx = new ChannelDbContext(options))
        {
            await ctx.Database.MigrateAsync();
        }

        await AssertHistoryAppliedAsync(connStr);
        await AssertTablesExistAsync(
            connStr,
            new[] { "channels", "webhook_events", "product_mappings", "channel_outbox_messages" }
        );
        await AssertConstraintsExistAsync(
            connStr,
            new[]
            {
                "pk_channels",
                "pk_webhook_events",
                "pk_product_mappings",
                "pk_channel_outbox_messages",
                "ck_product_mappings_method",
            }
        );
        await AssertIndexesExistAsync(
            connStr,
            new[]
            {
                "ux_webhook_events_channel_provider_event",
                "ux_product_mappings_channel_external_sku",
                "ix_channel_outbox_messages_pending",
            }
        );
    }

    [Fact]
    public async Task StockSyncMigration_AppliesAndLeavesNamedObjects()
    {
        var dbName = "smoke_stocksync_" + Guid.NewGuid().ToString("N")[..8];
        var connStr = await _postgres.CreateDatabaseAsync(dbName);

        var options = new DbContextOptionsBuilder<StockSyncDbContext>()
            .UseNpgsql(connStr, npg => npg.MigrationsAssembly("ShopFlow.StockSync.Infrastructure"))
            .Options;

        await using (var ctx = new StockSyncDbContext(options))
        {
            await ctx.Database.MigrateAsync();
        }

        await AssertHistoryAppliedAsync(connStr);
        await AssertTablesExistAsync(
            connStr,
            new[] { "stock_sync_sku_flag", "stock_sync_push_log", "stock_sync_outbox_messages" }
        );
        await AssertConstraintsExistAsync(
            connStr,
            new[]
            {
                "pk_stock_sync_sku_flag",
                "pk_stock_sync_push_log",
                "pk_stock_sync_outbox_messages",
                "ck_stock_sync_push_log_status",
            }
        );
        await AssertIndexesExistAsync(
            connStr,
            new[]
            {
                "ux_stock_sync_push_log_idempotency",
                "ix_stock_sync_push_log_tenant_channel_pushed",
                "ix_stock_sync_outbox_messages_pending",
            }
        );
    }

    private static async Task AssertColumnExistsAsync(
        string connStr,
        string tableName,
        string columnName
    )
    {
        await using var conn = new NpgsqlConnection(connStr);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText =
            "SELECT COUNT(*) FROM information_schema.columns "
            + "WHERE table_name = @t AND column_name = @c";
        cmd.Parameters.AddWithValue("t", tableName);
        cmd.Parameters.AddWithValue("c", columnName);
        var count = (long)(await cmd.ExecuteScalarAsync())!;
        count.Should().Be(1, $"expected column '{columnName}' on table '{tableName}' to exist");
    }

    private static async Task AssertHistoryAppliedAsync(string connStr)
    {
        // EF Core's default Npgsql migrations-history table name is
        // "__EFMigrationsHistory" (PascalCase, quoted because of the case).
        // shopflow-migrate (production migration runner) and the test
        // fixtures both leave this at the default — see
        // docs/solutions/2026-05-13-ef9-pendingmodelchangeswarning-with-hand-authored-migrations.md.
        await using var conn = new NpgsqlConnection(connStr);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM \"__EFMigrationsHistory\"";
        var count = (long)(await cmd.ExecuteScalarAsync())!;
        count
            .Should()
            .BeGreaterThanOrEqualTo(
                1,
                "MigrateAsync() must record at least one row in __EFMigrationsHistory "
                    + "— silent no-op detection per docs/solutions/2026-05-10-ef-migration-needs-attributes.md"
            );
    }

    private static async Task AssertTablesExistAsync(string connStr, string[] tableNames)
    {
        await using var conn = new NpgsqlConnection(connStr);
        await conn.OpenAsync();
        foreach (var table in tableNames)
        {
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT to_regclass(@n)::text";
            cmd.Parameters.AddWithValue("n", table);
            var result = await cmd.ExecuteScalarAsync();
            result
                .Should()
                .NotBe(DBNull.Value, $"expected table '{table}' to exist after migration");
        }
    }

    private static async Task AssertConstraintsExistAsync(string connStr, string[] constraintNames)
    {
        await using var conn = new NpgsqlConnection(connStr);
        await conn.OpenAsync();
        foreach (var name in constraintNames)
        {
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT COUNT(*) FROM pg_constraint WHERE conname = @n";
            cmd.Parameters.AddWithValue("n", name);
            var count = (long)(await cmd.ExecuteScalarAsync())!;
            count.Should().Be(1, $"expected constraint '{name}' to exist after migration");
        }
    }

    private static async Task AssertIndexesExistAsync(string connStr, string[] indexNames)
    {
        await using var conn = new NpgsqlConnection(connStr);
        await conn.OpenAsync();
        foreach (var name in indexNames)
        {
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT COUNT(*) FROM pg_indexes WHERE indexname = @n";
            cmd.Parameters.AddWithValue("n", name);
            var count = (long)(await cmd.ExecuteScalarAsync())!;
            count.Should().Be(1, $"expected index '{name}' to exist after migration");
        }
    }
}
