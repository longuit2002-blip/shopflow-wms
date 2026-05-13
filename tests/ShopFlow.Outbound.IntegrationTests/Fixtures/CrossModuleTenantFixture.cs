using Microsoft.EntityFrameworkCore;
using Npgsql;
using ShopFlow.Inventory.Infrastructure;
using ShopFlow.Outbound.Infrastructure;
using ShopFlow.SharedKernel.Application;
using ShopFlow.SharedKernel.Application.Ports;
using ShopFlow.SharedKernel.Domain;
using Testcontainers.PostgreSql;

namespace ShopFlow.Outbound.IntegrationTests.Fixtures;

/// <summary>
/// Sprint-3-redux U9 — Testcontainers Postgres fixture for the cross-module
/// reservation flow test. Provisions ONE tenant database and applies BOTH
/// the Outbound and Inventory module migrations to that single physical
/// database, matching the realistic production shape under ADR-0003
/// (database-per-tenant, all modules' schemas in the same DB).
/// </summary>
/// <remarks>
/// <para>This pattern is unlocked by Sprint-2.5's per-module outbox-table
/// prefix (<c>inbound_outbox_messages</c> / <c>inventory_outbox_messages</c>
/// / <c>outbound_outbox_messages</c>). Before Sprint-2.5, applying two
/// modules' migrations to the same DB collided on a shared
/// <c>outbox_messages</c> table.</para>
///
/// <para>Mirrors Sprint-2.5 U3's <c>InboundToInventoryFlowTests</c> shape
/// (the first multi-module shared-DB integration test): one container per
/// test class, one tenant DB per <see cref="ProvisionTenantAsync"/> call.
/// The <see cref="ProvisionedCrossModuleTenant"/> wrapper exposes both the
/// Outbound and Inventory <c>DbContextOptions</c> so tests can build per-
/// module DbContexts pointing at the same physical DB.</para>
///
/// <para>Per-PR speed: a fresh container start adds ~3-5s of fixed cost,
/// amortised across all tests in the collection. The single tenant DB is
/// re-provisioned per test class (clean slate per fixture instance).</para>
/// </remarks>
public sealed class CrossModuleTenantFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder()
        .WithImage("postgres:16")
        .WithDatabase("postgres")
        .WithUsername("postgres")
        .WithPassword("postgres")
        .Build();

    public string AdminConnectionString { get; private set; } = string.Empty;

    public async Task InitializeAsync()
    {
        await _container.StartAsync();
        AdminConnectionString = _container.GetConnectionString();
    }

    public async Task DisposeAsync()
    {
        await _container.DisposeAsync();
    }

    /// <summary>
    /// Provision a fresh tenant database and apply BOTH the Outbound and
    /// Inventory module migrations to it. Returns a wrapper carrying the
    /// tenant info + both per-module <c>DbContextOptions</c>.
    /// </summary>
    public async Task<ProvisionedCrossModuleTenant> ProvisionTenantAsync(
        string slug,
        CancellationToken ct = default
    )
    {
        var dbName = $"shopflow_t_xmod_{slug}_{Guid.NewGuid().ToString("N")[..8]}";
        await using (var admin = new NpgsqlConnection(AdminConnectionString))
        {
            await admin.OpenAsync(ct);
            await using var cmd = admin.CreateCommand();
            cmd.CommandText = $"CREATE DATABASE \"{dbName}\"";
            await cmd.ExecuteNonQueryAsync(ct);
        }

        var connStr = new NpgsqlConnectionStringBuilder(AdminConnectionString)
        {
            Database = dbName,
        }.ConnectionString;

        // Apply Outbound migrations first — the order doesn't actually
        // matter (no foreign keys cross the module boundary by ADR-0003)
        // but applying both in this thread before any test code runs
        // means the __EFMigrationsHistory row count == sum-of-modules.
        var outboundOptions = new DbContextOptionsBuilder<OutboundDbContext>()
            .UseNpgsql(connStr, npg => npg.MigrationsAssembly("ShopFlow.Outbound.Infrastructure"))
            .Options;
        await using (var ctx = new OutboundDbContext(outboundOptions))
        {
            await ctx.Database.MigrateAsync(ct);
        }

        var inventoryOptions = new DbContextOptionsBuilder<InventoryDbContext>()
            .UseNpgsql(connStr, npg => npg.MigrationsAssembly("ShopFlow.Inventory.Infrastructure"))
            .Options;
        await using (var ctx = new InventoryDbContext(inventoryOptions))
        {
            await ctx.Database.MigrateAsync(ct);
        }

        var info = new TenantInfo(
            Id: Guid.NewGuid(),
            Slug: slug,
            DbName: dbName,
            DbConnectionString: connStr,
            Region: "ap-southeast-1",
            Tier: "free",
            Status: TenantStatus.Ready
        );

        return new ProvisionedCrossModuleTenant(info, outboundOptions, inventoryOptions, connStr);
    }

    /// <summary>
    /// Seed one <c>stock_items</c> row with the given starting
    /// <c>available</c> stock. Reservations against this SKU update the
    /// row in place per the Sprint-1-redux conditional-CTE pattern.
    /// </summary>
    public static async Task SeedStockAsync(
        ProvisionedCrossModuleTenant tenant,
        string sku,
        int available,
        CancellationToken ct = default
    )
    {
        await using var conn = new NpgsqlConnection(tenant.ConnectionString);
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO stock_items (sku, available, reserved, created_at)
            VALUES (@sku, @avail, 0, @now)
            """;
        cmd.Parameters.AddWithValue("sku", sku);
        cmd.Parameters.AddWithValue("avail", available);
        cmd.Parameters.AddWithValue("now", DateTime.UtcNow);
        await cmd.ExecuteNonQueryAsync(ct);
    }
}

[CollectionDefinition(Name)]
public sealed class CrossModuleTenantCollection : ICollectionFixture<CrossModuleTenantFixture>
{
    public const string Name = "CrossModuleTenant";
}

/// <summary>
/// One provisioned tenant carrying BOTH modules' <c>DbContextOptions</c>
/// — Outbound and Inventory point at the same physical Postgres database
/// per ADR-0003.
/// </summary>
public sealed record ProvisionedCrossModuleTenant(
    TenantInfo Info,
    DbContextOptions<OutboundDbContext> OutboundOptions,
    DbContextOptions<InventoryDbContext> InventoryOptions,
    string ConnectionString
)
{
    public RequestContext BuildRequestContext()
    {
        var rc = new RequestContext();
        rc.Bind(Info, Guid.NewGuid().ToString("N"), userId: null);
        return rc;
    }
}
