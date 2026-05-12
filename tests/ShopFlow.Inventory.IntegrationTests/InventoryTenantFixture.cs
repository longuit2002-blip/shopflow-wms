using Microsoft.EntityFrameworkCore;
using Npgsql;
using ShopFlow.Inventory.Infrastructure;
using ShopFlow.SharedKernel.Application;
using ShopFlow.SharedKernel.Application.Ports;
using ShopFlow.SharedKernel.Domain;
using Testcontainers.PostgreSql;

namespace ShopFlow.Inventory.IntegrationTests;

/// <summary>
/// Shared Testcontainers Postgres fixture for the Inventory integration
/// suite. Each test class consumes the same container (startup cost
/// amortised) and provisions one or more fresh per-test tenant DBs via
/// <see cref="ProvisionTenantAsync"/>.
/// </summary>
public sealed class InventoryTenantFixture : IAsyncLifetime
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
    /// Provision a fresh tenant DB, run the Inventory schema migration
    /// against it, and return a <see cref="ProvisionedTenant"/> wrapping
    /// the connection string + a <see cref="TenantInfo"/>.
    /// </summary>
    public async Task<ProvisionedTenant> ProvisionTenantAsync(
        string slug,
        CancellationToken ct = default
    )
    {
        var dbName = $"shopflow_t_{slug}_{Guid.NewGuid().ToString("N")[..8]}";
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

        var options = new DbContextOptionsBuilder<InventoryDbContext>()
            .UseNpgsql(connStr, npg => npg.MigrationsAssembly("ShopFlow.Inventory.Infrastructure"))
            .Options;

        await using (var ctx = new InventoryDbContext(options))
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

        return new ProvisionedTenant(info, options);
    }

    /// <summary>
    /// Seed one <c>stock_items</c> row with the given starting stock.
    /// </summary>
    public async Task SeedStockAsync(
        ProvisionedTenant tenant,
        string sku,
        int available,
        CancellationToken ct = default
    )
    {
        await using var conn = new NpgsqlConnection(tenant.Info.DbConnectionString);
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
public sealed class InventoryTenantCollection : ICollectionFixture<InventoryTenantFixture>
{
    public const string Name = "InventoryTenant";
}

/// <summary>
/// One provisioned tenant: the catalog metadata plus the DbContext options
/// already bound to that tenant's database.
/// </summary>
public sealed record ProvisionedTenant(TenantInfo Info, DbContextOptions<InventoryDbContext> Options)
{
    public string ConnectionString => Info.DbConnectionString;

    /// <summary>
    /// Build a <see cref="RequestContext"/> bound to this tenant. Tests
    /// driving repository code under the real RequestContext / outbox
    /// stamping path use this.
    /// </summary>
    public RequestContext BuildRequestContext()
    {
        var rc = new RequestContext();
        rc.Bind(Info, Guid.NewGuid().ToString("N"), userId: null);
        return rc;
    }
}
