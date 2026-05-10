using FluentAssertions;
using Npgsql;

namespace ShopFlow.Inventory.IntegrationTests;

[Collection("Integration")]
[Trait("Category", "Integration")]
public sealed class RlsPolicyTests
{
    private readonly PostgresFixture _fixture;

    public RlsPolicyTests(PostgresFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task ReservationsLedger_ConnectionScopedToTenantA_DoesNotSeeTenantBRows()
    {
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();

        // Seed one row per tenant via a privileged connection (no SET LOCAL,
        // so the writes succeed regardless of the policy).
        await SeedReservation(tenantA, "SKU-A");
        await SeedReservation(tenantB, "SKU-B");

        // Open a non-superuser session that respects RLS. The default
        // postgres test user is superuser and bypasses RLS, so we explicitly
        // create a non-bypass role for the test. Done idempotently.
        await EnsureNonBypassRole();

        await using var conn = new NpgsqlConnection(_fixture.ConnectionString);
        await conn.OpenAsync();

        await using (var setRole = conn.CreateCommand())
        {
            setRole.CommandText = "SET ROLE shopflow_app;";
            await setRole.ExecuteNonQueryAsync();
        }

        await using (var setTenant = conn.CreateCommand())
        {
            setTenant.CommandText = $"SET LOCAL app.tenant_id = '{tenantA}';";
            await setTenant.ExecuteNonQueryAsync();
        }

        await using var query = conn.CreateCommand();
        query.CommandText = "SELECT COUNT(*) FROM reservations_ledger WHERE tenant_id = @t;";
        query.Parameters.AddWithValue("t", tenantB);

        var visibleTenantBCount = (long)(await query.ExecuteScalarAsync() ?? 0L);
        visibleTenantBCount
            .Should()
            .Be(0, "RLS policy should hide tenant B rows from a tenant A session");
    }

    private async Task SeedReservation(Guid tenantId, string sku)
    {
        await using var conn = new NpgsqlConnection(_fixture.ConnectionString);
        await conn.OpenAsync();

        // Ensure parent stock_items row exists for the FK-less ledger
        // (the ledger has no FK at the schema level, but seeding both
        // mirrors the production write order).
        await using (var insertStock = conn.CreateCommand())
        {
            insertStock.CommandText = """
                INSERT INTO stock_items
                    (tenant_id, sku, id, name, category, total_qty,
                     allocated_qty, safety_threshold, created_at)
                VALUES
                    (@tenant, @sku, @id, 'Test', null, 100, 0, 0, NOW())
                ON CONFLICT (tenant_id, sku) DO NOTHING;
                """;
            insertStock.Parameters.AddWithValue("tenant", tenantId);
            insertStock.Parameters.AddWithValue("sku", sku);
            insertStock.Parameters.AddWithValue("id", Guid.NewGuid());
            await insertStock.ExecuteNonQueryAsync();
        }

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO reservations_ledger
                (tenant_id, sku, id, qty, order_id, status, reserved_at, expires_at)
            VALUES
                (@tenant, @sku, @id, 1, @order, 'Active',
                 NOW(), NOW() + INTERVAL '15 minutes');
            """;
        cmd.Parameters.AddWithValue("tenant", tenantId);
        cmd.Parameters.AddWithValue("sku", sku);
        cmd.Parameters.AddWithValue("id", Guid.NewGuid());
        cmd.Parameters.AddWithValue("order", Guid.NewGuid());
        await cmd.ExecuteNonQueryAsync();
    }

    private async Task EnsureNonBypassRole()
    {
        await using var conn = new NpgsqlConnection(_fixture.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            DO $$
            BEGIN
                IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'shopflow_app') THEN
                    CREATE ROLE shopflow_app NOLOGIN NOBYPASSRLS;
                END IF;
            END$$;
            GRANT SELECT, INSERT, UPDATE ON ALL TABLES IN SCHEMA public TO shopflow_app;
            """;
        await cmd.ExecuteNonQueryAsync();
    }
}
