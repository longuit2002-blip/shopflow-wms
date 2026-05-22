using Microsoft.AspNetCore.Mvc.Testing;
using Npgsql;
using ShopFlow.Auth.IntegrationTests.Authorization;
using Testcontainers.PostgreSql;

namespace ShopFlow.Inbound.IntegrationTests.Authorization;

/// <summary>
/// Sprint-10.5 U4 — boots Inbound.Api in-process against a
/// Testcontainers Postgres instance and provisions a per-fixture tenant
/// DB carrying the Inbound schema. Exposes a configured
/// <see cref="HttpClient"/> + the <c>NarrowedJwtBuilder</c> wired against
/// the same <c>Auth:DevSecret</c> the kernel <c>JwtBearer</c> validator
/// reads at host boot.
///
/// <para>Net-new HTTP test infrastructure (KTD6): Inbound.IntegrationTests
/// previously held no <c>WebApplicationFactory&lt;Program&gt;</c>
/// callers — the Sprint-2-redux suite drives repositories + the
/// orchestration service directly. This fixture extends the project with
/// the WAF-backed shape so <see cref="Inbound403Tests"/> can submit real
/// HTTP calls against the per-action <c>[Authorize(Policy=...)]</c>
/// gates Sprint-10 attached.</para>
///
/// <para>Inbound.Api's <c>Program.cs</c> was extended with the
/// <c>public partial class Program;</c> declaration in the same Sprint-10.5
/// U4 commit so <see cref="WebApplicationFactory{TEntryPoint}"/> can
/// boot it in-process.</para>
///
/// <para>Skip-marked locally per Sprint-1+ posture.</para>
/// </summary>
public sealed class InboundAuthorizationFixture : IAsyncLifetime
{
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

    public WebApplicationFactory<Program> Factory => _factory
        ?? throw new InvalidOperationException("Fixture not initialized.");

    public HttpClient HttpClient => Factory.CreateClient();

    public async Task InitializeAsync()
    {
        await _container.StartAsync();
        var admin = _container.GetConnectionString();
        ControlPlaneConnectionString = admin;

        var dbName = $"shopflow_inb_{Guid.NewGuid().ToString("N")[..8]}";
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
                new NpgsqlConnectionStringBuilder(admin) { Database = "{Database}" }.ConnectionString
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
public sealed class InboundAuthorizationCollection : ICollectionFixture<InboundAuthorizationFixture>
{
    public const string Name = "InboundAuthorization";
}
