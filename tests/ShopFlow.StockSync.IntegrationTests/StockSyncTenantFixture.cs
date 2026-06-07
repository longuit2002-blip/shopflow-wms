using Microsoft.EntityFrameworkCore;
using Npgsql;
using ShopFlow.SharedKernel.Application.Ports;
using ShopFlow.SharedKernel.Domain;
using ShopFlow.StockSync.Infrastructure;
using Testcontainers.PostgreSql;

namespace ShopFlow.StockSync.IntegrationTests;

/// <summary>
/// Sprint-5 plan U5 / U9 — Testcontainers Postgres fixture for the
/// StockSync integration suite. Mirrors
/// <c>InventoryTenantFixture</c> verbatim: one shared container per
/// test class, fresh per-test tenant DBs with the StockSync schema
/// migration applied. Each <see cref="ProvisionTenantAsync"/> call
/// produces a <see cref="StockSyncProvisionedTenant"/> whose
/// connection string is rooted at the per-tenant database.
/// </summary>
public sealed class StockSyncTenantFixture : IAsyncLifetime
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

    public async Task<StockSyncProvisionedTenant> ProvisionTenantAsync(
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

        var options = new DbContextOptionsBuilder<StockSyncDbContext>()
            .UseNpgsql(connStr, npg => npg.MigrationsAssembly("ShopFlow.StockSync.Infrastructure"))
            .Options;

        await using (var ctx = new StockSyncDbContext(options))
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

        return new StockSyncProvisionedTenant(info, options);
    }
}

[CollectionDefinition(Name)]
public sealed class StockSyncTenantCollection : ICollectionFixture<StockSyncTenantFixture>
{
    public const string Name = "StockSyncTenant";
}

public sealed record StockSyncProvisionedTenant(
    TenantInfo Info,
    DbContextOptions<StockSyncDbContext> Options
)
{
    public string ConnectionString => Info.DbConnectionString;
}
