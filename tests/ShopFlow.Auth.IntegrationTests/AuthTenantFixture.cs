using Microsoft.EntityFrameworkCore;
using Npgsql;
using ShopFlow.Auth.Infrastructure;
using Testcontainers.PostgreSql;

namespace ShopFlow.Auth.IntegrationTests;

/// <summary>
/// Shared Testcontainers Postgres fixture for the Auth integration
/// suite. Each test class consumes the same container (startup cost
/// amortised) and provisions fresh per-test tenant DBs via
/// <see cref="ProvisionTenantAsync"/>. Mirrors the
/// <c>InventoryTenantFixture</c> shape from Sprint-1-redux.
/// </summary>
public sealed class AuthTenantFixture : IAsyncLifetime
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
    /// Provision a fresh tenant DB, run the Auth schema migration
    /// (<c>20260520000001_AddUsers</c>) against it, and return a
    /// <see cref="ProvisionedAuthTenant"/> wrapping the connection
    /// string + bound <see cref="DbContextOptions{AuthDbContext}"/>.
    /// </summary>
    public async Task<ProvisionedAuthTenant> ProvisionTenantAsync(
        string slug,
        CancellationToken ct = default)
    {
        var dbName = $"shopflow_auth_{slug}_{Guid.NewGuid().ToString("N")[..8]}";
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

        var options = new DbContextOptionsBuilder<AuthDbContext>()
            .UseNpgsql(connStr, npg => npg.MigrationsAssembly("ShopFlow.Auth.Infrastructure"))
            .Options;

        await using (var ctx = new AuthDbContext(options))
        {
            await ctx.Database.MigrateAsync(ct);
        }

        return new ProvisionedAuthTenant(slug, connStr, options);
    }
}

[CollectionDefinition(Name)]
public sealed class AuthTenantCollection : ICollectionFixture<AuthTenantFixture>
{
    public const string Name = "AuthTenant";
}

/// <summary>
/// One provisioned tenant: the slug + connection string + DbContext
/// options bound to that tenant's database. Tests construct
/// <see cref="AuthDbContext"/> instances against
/// <see cref="Options"/> directly (no DI scope required — these are
/// repository-level tests, not full request-pipeline tests).
/// </summary>
public sealed record ProvisionedAuthTenant(
    string Slug,
    string ConnectionString,
    DbContextOptions<AuthDbContext> Options);
