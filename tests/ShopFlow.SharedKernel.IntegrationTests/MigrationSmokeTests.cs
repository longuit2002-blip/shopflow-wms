using Microsoft.EntityFrameworkCore;
using Npgsql;
using ShopFlow.ControlPlane.Infrastructure;
using ShopFlow.Inventory.Infrastructure;

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
            new[]
            {
                "pk_tenants",
                "pk_tenant_events",
                "pk_channel_connections",
            }
        );
        await AssertIndexesExistAsync(
            connStr,
            new[] { "ux_tenants_slug", "ux_tenants_db_name" }
        );
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
                "outbox_messages",
            }
        );
        await AssertConstraintsExistAsync(
            connStr,
            new[]
            {
                "pk_stock_items",
                "pk_reservations_ledger",
                "pk_stock_adjustments",
                "pk_outbox_messages",
                "fk_reservations_stock_items_sku",
                "fk_stock_adjustments_stock_items_sku",
            }
        );
        await AssertIndexesExistAsync(
            connStr,
            new[] { "ux_reservations_order_id", "ix_reservations_status_expires_at" }
        );
    }

    private static async Task AssertHistoryAppliedAsync(string connStr)
    {
        await using var conn = new NpgsqlConnection(connStr);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM __ef_migrations_history";
        var count = (long)(await cmd.ExecuteScalarAsync())!;
        count.Should()
            .BeGreaterThanOrEqualTo(
                1,
                "MigrateAsync() must record at least one row in __ef_migrations_history "
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
            result.Should()
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
