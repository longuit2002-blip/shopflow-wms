using Microsoft.EntityFrameworkCore;
using Npgsql;
using ShopFlow.Inbound.Infrastructure;
using ShopFlow.SharedKernel.Application;
using ShopFlow.SharedKernel.Application.Ports;
using ShopFlow.SharedKernel.Domain;
using Testcontainers.PostgreSql;

namespace ShopFlow.Inbound.IntegrationTests;

/// <summary>
/// Shared Testcontainers Postgres fixture for the Inbound integration
/// suite. Mirrors <c>ShopFlow.Inventory.IntegrationTests.InventoryTenantFixture</c>
/// — one container per test class collection, fresh tenant DB per
/// <see cref="ProvisionTenantAsync"/> call.
/// </summary>
public sealed class InboundTenantFixture : IAsyncLifetime
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

    public async Task<ProvisionedInboundTenant> ProvisionTenantAsync(
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

        var options = new DbContextOptionsBuilder<InboundDbContext>()
            .UseNpgsql(connStr, npg => npg.MigrationsAssembly("ShopFlow.Inbound.Infrastructure"))
            .Options;

        await using (var ctx = new InboundDbContext(options))
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

        return new ProvisionedInboundTenant(info, options);
    }
}

[CollectionDefinition(Name)]
public sealed class InboundTenantCollection : ICollectionFixture<InboundTenantFixture>
{
    public const string Name = "InboundTenant";
}

public sealed record ProvisionedInboundTenant(
    TenantInfo Info,
    DbContextOptions<InboundDbContext> Options
)
{
    public string ConnectionString => Info.DbConnectionString;

    public RequestContext BuildRequestContext()
    {
        var rc = new RequestContext();
        rc.Bind(Info, Guid.NewGuid().ToString("N"), userId: null);
        return rc;
    }
}
