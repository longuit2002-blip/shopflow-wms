using Microsoft.AspNetCore.Mvc.Testing;
using Npgsql;
using ShopFlow.Auth.IntegrationTests.Authorization;
using Testcontainers.PostgreSql;

namespace ShopFlow.Inventory.IntegrationTests.Authorization;

/// <summary>
/// Sprint-10.5 U4 — boots Inventory.Api in-process against a
/// Testcontainers Postgres instance and provisions a per-fixture tenant
/// DB carrying the Inventory schema. Exposes a configured
/// <see cref="HttpClient"/> + the <c>NarrowedJwtBuilder</c> wired against
/// the same <c>Auth:DevSecret</c> the kernel <c>JwtBearer</c> validator
/// reads at host boot.
///
/// <para>Net-new HTTP test infrastructure (KTD6): Inventory.IntegrationTests
/// previously held no <c>WebApplicationFactory&lt;Program&gt;</c>
/// callers — the Sprint-1-redux suite drives repositories directly.
/// This fixture extends the project with the WAF-backed shape so
/// <see cref="Inventory403Tests"/> can submit real HTTP calls against
/// the per-action <c>[Authorize(Policy=...)]</c> gates Sprint-10
/// attached.</para>
///
/// <para>Skip-marked locally per Sprint-1+ posture: the tests this
/// fixture supports are gated by Docker daemon availability. CI's
/// nightly + per-PR Docker-backed job removes the Skip.</para>
/// </summary>
public sealed class InventoryAuthorizationFixture : IAsyncLifetime
{
    /// <summary>
    /// Shared HS256 signing secret for the test JWTs. MUST stay 32+
    /// UTF-8 bytes — the kernel validator + <see cref="NarrowedJwtBuilder"/>
    /// both enforce the minimum. Single source of truth for fixture +
    /// builder + WAF <c>UseSetting</c> call.
    /// </summary>
    public const string DevSecret = "shopflow-dev-only-do-not-use-in-prod-32bytes!!";

    /// <summary>Shared <c>iss</c> claim value — matches the kernel validator default.</summary>
    public const string Issuer = "shopflow-dev";

    /// <summary>Shared <c>aud</c> claim value — matches the kernel validator default.</summary>
    public const string Audience = "shopflow-api";

    /// <summary>Conventional tenant slug for single-tenant fixtures. Encoded on the JWT <c>tenant_slug</c> claim.</summary>
    public const string TenantSlug = "test-tenant";

    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder()
        .WithImage("postgres:16")
        .WithDatabase("postgres")
        .WithUsername("postgres")
        .WithPassword("postgres")
        .Build();

    private WebApplicationFactory<Program>? _factory;

    /// <summary>Per-tenant DB connection string (provisioned in <see cref="InitializeAsync"/>).</summary>
    public string TenantConnectionString { get; private set; } = string.Empty;

    /// <summary>Control-plane DB connection string (admin DB on the container; resolves the tenant catalog).</summary>
    public string ControlPlaneConnectionString { get; private set; } = string.Empty;

    /// <summary>The shared JWT builder wired against this fixture's <see cref="DevSecret"/>.</summary>
    public NarrowedJwtBuilder JwtBuilder { get; private set; } = default!;

    /// <summary>The booted <see cref="WebApplicationFactory{TEntryPoint}"/> for Inventory.Api.</summary>
    public WebApplicationFactory<Program> Factory =>
        _factory ?? throw new InvalidOperationException("Fixture not initialized.");

    /// <summary>Configured HttpClient against the in-process Inventory.Api host.</summary>
    public HttpClient HttpClient => Factory.CreateClient();

    public async Task InitializeAsync()
    {
        await _container.StartAsync();

        var admin = _container.GetConnectionString();
        ControlPlaneConnectionString = admin;

        // Provision a fresh per-tenant DB. CI tier runs `shopflow-migrate provision`
        // against this DB to apply the Inventory schema + RolePermissionsSeed
        // (Sprint-9 U12) which Owner-seeds all 24 keys. Local dev machines skip
        // the tests per Sprint-1+ posture so this is documentation-only.
        var dbName = $"shopflow_inv_{Guid.NewGuid().ToString("N")[..8]}";
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
public sealed class InventoryAuthorizationCollection
    : ICollectionFixture<InventoryAuthorizationFixture>
{
    public const string Name = "InventoryAuthorization";
}
