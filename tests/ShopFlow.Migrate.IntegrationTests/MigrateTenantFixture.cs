using Microsoft.EntityFrameworkCore;
using Npgsql;
using ShopFlow.Auth.Infrastructure;
using ShopFlow.Inventory.Infrastructure;
using Testcontainers.PostgreSql;
using Xunit;

namespace ShopFlow.Migrate.IntegrationTests;

/// <summary>
/// Shared Testcontainers Postgres fixture for the shopflow-migrate
/// integration suite (Sprint-8.5 U11). Mirrors Sprint-8 U3's
/// <c>AuthTenantFixture</c> shape — single container amortised across
/// every test class in the collection; per-test fresh tenant DB.
/// </summary>
/// <remarks>
/// <para>Unlike <c>AuthTenantFixture</c>, this fixture provisions BOTH
/// the Inventory and Auth migrations against each tenant DB — the
/// OwnerSeed flow inserts into the <c>users</c> table (Auth migration)
/// after the tenant is fully provisioned. The migration order
/// (Inventory first, Auth second) mirrors
/// <c>tools/shopflow-migrate/Program.cs</c>'s
/// <c>IModuleMigrationRegistry</c> registration sequence.</para>
/// </remarks>
public sealed class MigrateTenantFixture : IAsyncLifetime
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
    /// Provision a fresh tenant DB, run Inventory + Auth migrations
    /// against it via the per-module DbContexts, and return the
    /// connection string for OwnerSeed.SeedAsync.
    /// </summary>
    public async Task<ProvisionedMigrateTenant> ProvisionTenantAsync(
        string slug,
        CancellationToken ct = default)
    {
        var dbName = $"shopflow_migrate_{slug}_{Guid.NewGuid().ToString("N")[..8]}";
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

        // Inventory migrations first — matches IModuleMigrationRegistry
        // registration order in tools/shopflow-migrate/Program.cs.
        var inventoryOptions = new DbContextOptionsBuilder<InventoryDbContext>()
            .UseNpgsql(connStr, npg => npg.MigrationsAssembly("ShopFlow.Inventory.Infrastructure"))
            .Options;
        await using (var ctx = new InventoryDbContext(inventoryOptions))
        {
            await ctx.Database.MigrateAsync(ct);
        }

        var authOptions = new DbContextOptionsBuilder<AuthDbContext>()
            .UseNpgsql(connStr, npg => npg.MigrationsAssembly("ShopFlow.Auth.Infrastructure"))
            .Options;
        await using (var ctx = new AuthDbContext(authOptions))
        {
            await ctx.Database.MigrateAsync(ct);
        }

        return new ProvisionedMigrateTenant(slug, connStr);
    }
}

[CollectionDefinition(Name)]
public sealed class MigrateTenantCollection : ICollectionFixture<MigrateTenantFixture>
{
    public const string Name = "MigrateTenant";
}

/// <summary>
/// One provisioned tenant: the slug + connection string. Tests that
/// need a DbContext build their own — this fixture's only job is to
/// hand back a fully-migrated tenant DB.
/// </summary>
public sealed record ProvisionedMigrateTenant(string Slug, string ConnectionString);
