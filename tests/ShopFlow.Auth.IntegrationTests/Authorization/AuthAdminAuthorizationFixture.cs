using Microsoft.AspNetCore.Mvc.Testing;
using Npgsql;
using Testcontainers.PostgreSql;

namespace ShopFlow.Auth.IntegrationTests.Authorization;

/// <summary>
/// Sprint-10.5 U4 — boots Auth.Api in-process against a Testcontainers
/// Postgres instance + provisions a per-fixture tenant DB. Extends the
/// Sprint-8 <c>AuthControllerEndpointTests</c> WAF pattern with the
/// narrowed-JWT path for the 9 <see cref="AuthAdmin403Tests"/> facts.
///
/// <para>Auth.Api's <c>Program.cs</c> already exposes
/// <c>public partial class Program</c> from Sprint-8; no source change
/// needed in U4.</para>
///
/// <para>Skip-marked locally per Sprint-1+ posture.</para>
/// </summary>
public sealed class AuthAdminAuthorizationFixture : IAsyncLifetime
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

        var dbName = $"shopflow_auth_{Guid.NewGuid().ToString("N")[..8]}";
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
public sealed class AuthAdminAuthorizationCollection : ICollectionFixture<AuthAdminAuthorizationFixture>
{
    public const string Name = "AuthAdminAuthorization";
}
