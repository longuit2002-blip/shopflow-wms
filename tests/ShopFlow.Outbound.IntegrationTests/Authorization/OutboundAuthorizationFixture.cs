using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Hosting;
using Npgsql;
using ShopFlow.Auth.IntegrationTests.Authorization;
using Testcontainers.PostgreSql;

namespace ShopFlow.Outbound.IntegrationTests.Authorization;

/// <summary>
/// Sprint-10.5 U4 — boots Outbound.Api in-process against a
/// Testcontainers Postgres instance and provisions a per-fixture tenant
/// DB. Exposes a configured <see cref="HttpClient"/> + the
/// <c>NarrowedJwtBuilder</c> wired against the same <c>Auth:DevSecret</c>
/// the kernel <c>JwtBearer</c> validator reads at host boot.
///
/// <para>Outbound already has <see cref="WebApplicationFactory{TEntryPoint}"/>
/// usage since Sprint-7 (see <c>OrdersListAndDetailEndpointTests</c> +
/// <c>OrdersSeedEndpointTests</c>). This fixture wraps the same shape
/// with the narrowed-JWT path for the 10 403 tests.</para>
///
/// <para>Critical: <see cref="UseEnvironment"/> is set to
/// <c>"Development"</c> so <c>OrdersController.SeedAsync</c>'s
/// <c>IsDevelopment()</c> guard returns true and the action is
/// reachable — otherwise the 403 test for the seed action would
/// instead receive 404 + <c>environment_not_dev</c>, masking the policy
/// enforcement check.</para>
///
/// <para>Skip-marked locally per Sprint-1+ posture; CI removes the Skip
/// via Docker-backed nightly + per-PR job.</para>
/// </summary>
public sealed class OutboundAuthorizationFixture : IAsyncLifetime
{
    /// <summary>Shared HS256 signing secret. 32+ UTF-8 bytes required by kernel + builder.</summary>
    public const string DevSecret = "shopflow-dev-only-do-not-use-in-prod-32bytes!!";

    public const string Issuer = "shopflow-dev";
    public const string Audience = "shopflow-api";
    public const string TenantSlug = "test-tenant";

    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder()
        .WithImage("postgres:16")
        .WithDatabase("postgres")
        .WithUsername("postgres")
        .WithPassword("postgres")
        .Build();

    private WebApplicationFactory<Program>? _factory;

    public string TenantConnectionString { get; private set; } = string.Empty;
    public string ControlPlaneConnectionString { get; private set; } = string.Empty;
    public NarrowedJwtBuilder JwtBuilder { get; private set; } = default!;

    public WebApplicationFactory<Program> Factory =>
        _factory ?? throw new InvalidOperationException("Fixture not initialized.");

    public HttpClient HttpClient => Factory.CreateClient();

    public async Task InitializeAsync()
    {
        await _container.StartAsync();
        var admin = _container.GetConnectionString();
        ControlPlaneConnectionString = admin;

        var dbName = $"shopflow_out_{Guid.NewGuid().ToString("N")[..8]}";
        await using (var conn = new NpgsqlConnection(admin))
        {
            await conn.OpenAsync();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = $"CREATE DATABASE \"{dbName}\"";
            await cmd.ExecuteNonQueryAsync();
        }

        TenantConnectionString = new NpgsqlConnectionStringBuilder(admin)
        {
            Database = dbName,
        }.ConnectionString;

        JwtBuilder = new NarrowedJwtBuilder(DevSecret, Issuer, Audience);

        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            // Sprint-10.5 U4 — Development env is REQUIRED so
            // OrdersController.SeedAsync's IsDevelopment() guard returns
            // true; otherwise the 403 test for /seed would receive 404
            // + environment_not_dev and the policy gate would never be
            // reached.
            b.UseEnvironment(Environments.Development);

            b.UseSetting("Auth:DevSecret", DevSecret);
            b.UseSetting("Auth:Issuer", Issuer);
            b.UseSetting("Auth:Audience", Audience);
            b.UseSetting("ConnectionStrings:Redis", "localhost:6379");
            b.UseSetting("MessageBus:Transport", "InMemory");
            b.UseSetting("ControlPlane:ConnectionString", ControlPlaneConnectionString);
            b.UseSetting(
                "ControlPlane:TenantTemplate",
                new NpgsqlConnectionStringBuilder(admin)
                {
                    Database = "{Database}",
                }.ConnectionString
            );
        });
    }

    public async Task DisposeAsync()
    {
        if (_factory is not null)
        {
            await _factory.DisposeAsync();
        }
        await _container.DisposeAsync();
    }
}

[CollectionDefinition(Name)]
public sealed class OutboundAuthorizationCollection
    : ICollectionFixture<OutboundAuthorizationFixture>
{
    public const string Name = "OutboundAuthorization";
}
