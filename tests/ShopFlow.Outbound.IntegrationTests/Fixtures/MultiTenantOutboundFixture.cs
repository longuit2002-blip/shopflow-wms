using Microsoft.EntityFrameworkCore;
using Npgsql;
using ShopFlow.Outbound.Domain;
using ShopFlow.Outbound.Infrastructure;
using ShopFlow.SharedKernel.Application;
using ShopFlow.SharedKernel.Application.Ports;
using ShopFlow.SharedKernel.Domain;
using Testcontainers.PostgreSql;

namespace ShopFlow.Outbound.IntegrationTests.Fixtures;

/// <summary>
/// Sprint-3-redux U8 — multi-tenant Testcontainers Postgres fixture for
/// the W5 scale gate. Provisions <c>N</c> tenant DBs (typically 3) and
/// applies the Outbound migrations to each, mirroring
/// <see cref="OutboundTenantFixture"/> but with a per-test-class
/// collection-scope so the 3-tenant provisioning cost is amortized across
/// the two scale-gate scenarios (happy + 5% pick-failure variant).
/// </summary>
/// <remarks>
/// <para>The plan U8 calls for "both Outbound + Inventory migrations
/// applied" so the auto-driver could exercise the saga's reservation hop.
/// In practice the scale gate bypasses the saga path: drivers directly
/// progress the Order row through the operator-facing pipeline (pick →
/// pack → ship). This sidesteps the per-tenant DbContext binding
/// complexity of running 3 concurrent saga instances under the
/// in-memory bus (covered by <c>SagaPerTenantBindingTests</c>) and keeps
/// the gate focused on what W5 actually measures: operator-pipeline
/// throughput under concurrent load with fairness across tenants.</para>
///
/// <para>Hardware caveat per Sprint-1-redux W3 precedent — dev laptop
/// measurements documented as such; production-CI re-validates the
/// absolute p99 numbers.</para>
/// </remarks>
public sealed class MultiTenantOutboundFixture : IAsyncLifetime
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
    /// Provision a fresh tenant DB, apply the Outbound migrations, and
    /// return a <see cref="ProvisionedOutboundTenant"/> wrapping the
    /// connection string + DbContext options. Same shape as
    /// <see cref="OutboundTenantFixture.ProvisionTenantAsync"/>.
    /// </summary>
    public async Task<ProvisionedOutboundTenant> ProvisionTenantAsync(
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

        // Cap Npgsql pool size at 25 per tenant DB. The Testcontainers
        // Postgres defaults to max_connections=100; 3 tenants × 25 = 75
        // active leaves headroom for the admin connection used in
        // provisioning + the warm-up phase. Without this cap the
        // default Npgsql max=100 saturates the server with 6 tenants
        // (happy+variant tests run back-to-back) and the second test's
        // provisioning trips "53300: sorry, too many clients already".
        var connStr = new NpgsqlConnectionStringBuilder(AdminConnectionString)
        {
            Database = dbName,
            MaxPoolSize = 25,
        }.ConnectionString;

        var options = new DbContextOptionsBuilder<OutboundDbContext>()
            .UseNpgsql(connStr, npg => npg.MigrationsAssembly("ShopFlow.Outbound.Infrastructure"))
            .Options;

        await using (var ctx = new OutboundDbContext(options))
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

        return new ProvisionedOutboundTenant(info, options, connStr);
    }

    /// <summary>
    /// Seed N pickers per tenant. The plan U8 spec calls for 5 pickers
    /// per tenant; tests are free to vary. Round-robin picker assignment
    /// in <c>PickWaveGeneratorService</c> spreads work evenly across them.
    /// </summary>
    public static async Task SeedPickersAsync(
        ProvisionedOutboundTenant tenant,
        IReadOnlyList<string> pickerIds,
        CancellationToken ct = default
    )
    {
        await using var ctx = new OutboundDbContext(tenant.Options);
        foreach (var pid in pickerIds)
        {
            ctx.Pickers.Add(Picker.Create(pid, $"Picker {pid}"));
        }
        await ctx.SaveChangesAsync(ct);
    }
}

[CollectionDefinition(Name)]
public sealed class MultiTenantOutboundCollection : ICollectionFixture<MultiTenantOutboundFixture>
{
    public const string Name = "MultiTenantOutbound";
}
