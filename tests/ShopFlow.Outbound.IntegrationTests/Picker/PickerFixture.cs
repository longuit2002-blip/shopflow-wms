using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Hosting;
using Npgsql;
using ShopFlow.Auth.IntegrationTests.Authorization;
using Testcontainers.PostgreSql;

namespace ShopFlow.Outbound.IntegrationTests.PickerE2E;

/// <summary>
/// Sprint-11 U3 — Docker-backed fixture for the Picker end-to-end happy-path
/// integration test (<see cref="PickerHappyPathTests"/>). Boots
/// <c>Outbound.Api</c> in-process against a Testcontainers Postgres
/// instance; provisions one tenant DB that hosts BOTH the Auth schema
/// (Sprint-9 <c>20260601000001_AddSprint9AuthSchema</c> + earlier
/// <c>20260520000001_AddUsers</c>) AND the Outbound schema (initial +
/// saga state + transitions) per Sprint-2.5's cross-module
/// shared-DB precedent (module-prefixed outbox tables avoid the
/// <c>outbox_messages</c> collision).
///
/// <para><strong>Chosen path: B — NarrowedJwtBuilder fallback (KTD4 + F4).</strong>
/// The dual-WAF strategy (Path A: <c>WebApplicationFactory&lt;auth::Program&gt;</c>
/// + <c>WebApplicationFactory&lt;Program&gt;</c> with <c>extern alias</c>) was
/// rejected at design time. Sprint-10.5 U4 already discovered that the
/// cross-test-project <c>ProjectReference</c> to <c>ShopFlow.Auth.Api</c>
/// transitively pulls <c>Auth.Api.dll</c> into the Outbound test assembly
/// and collides with this project's WAF target (the
/// <see cref="ShopFlow.Outbound.IntegrationTests.ShopFlow.Outbound.IntegrationTests.csproj"/>
/// comment block documents this verbatim). NarrowedJwtBuilder is linked as
/// a Compile item from <c>Auth.IntegrationTests/Authorization/</c> to avoid
/// the transitive cost. Following that established mitigation here keeps
/// the U3 fixture single-host, makes the dependency surface auditable, and
/// matches Sprint-10.5's pattern that the rest of the Authorization/
/// suite uses.</para>
///
/// <para><strong>Trade-off:</strong> "Picker logs in via POST /api/auth/login"
/// is NOT verified end-to-end in Path B. Instead, the test seeds the Picker
/// user directly via raw INSERT against the Auth schema (Argon2id password
/// hash optional; the JWT path doesn't read it) and mints the Picker JWT via
/// <see cref="NarrowedJwtBuilder.Build"/>. The kernel <c>JwtBearer</c> wired
/// by <c>AddShopFlowDefaults</c> validates the token at the Outbound.Api
/// host; the per-action <c>[Authorize(Policy = OutboundOrdersPickConfirm)]</c>
/// gate independently accepts the Picker baseline perm[] (Sprint-11 U1
/// seeded). Saga + audit chain remain verifiable.</para>
///
/// <para>Skip-marked locally per Sprint-1+ posture; CI removes the Skip via
/// the Docker-backed nightly + per-PR job (the chaos-nightly + integration
/// workflows already host the Testcontainers PG image pull).</para>
/// </summary>
public sealed class PickerFixture : IAsyncLifetime
{
    /// <summary>Shared HS256 signing secret. 32+ UTF-8 bytes required by
    /// the kernel <c>JwtBearer</c> validator. Mirrors
    /// <see cref="OutboundAuthorizationFixture.DevSecret"/> verbatim so the
    /// minted JWT validates against the WAF-booted Outbound host.</summary>
    public const string DevSecret = "shopflow-dev-only-do-not-use-in-prod-32bytes!!";

    public const string Issuer = "shopflow-dev";
    public const string Audience = "shopflow-api";
    public const string TenantSlug = "picker-tenant";

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

    /// <summary>Picker user id. Seeded by <see cref="InitializeAsync"/> via
    /// direct AuthDbContext INSERT after RolePermissionsSeed runs. The
    /// minted JWT carries this as the <c>sub</c> claim.</summary>
    public Guid PickerUserId { get; private set; } = Guid.NewGuid();

    public string PickerEmail => $"picker@{TenantSlug}.test";

    public WebApplicationFactory<Program> Factory =>
        _factory ?? throw new InvalidOperationException("Fixture not initialized.");

    public HttpClient HttpClient => Factory.CreateClient();

    public async Task InitializeAsync()
    {
        await _container.StartAsync();
        var admin = _container.GetConnectionString();
        ControlPlaneConnectionString = admin;

        // Sprint-2.5 precedent — provision ONE tenant DB that hosts both
        // Auth + Outbound schemas. Per-module outbox prefix
        // (auth_outbox_messages / outbound_outbox_messages) prevents the
        // legacy collision.
        var dbName = $"shopflow_pkr_{Guid.NewGuid().ToString("N")[..8]}";
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

        // CI-tier body (omitted from local skipped run):
        //   1. AuthDbContext.Database.MigrateAsync(TenantConnectionString)
        //      — applies AddUsers + AddSprint9AuthSchema migrations,
        //      creating users + role_permissions + 5 sibling tables.
        //   2. OutboundDbContext.Database.MigrateAsync(TenantConnectionString)
        //      — applies InitialOutboundSchema + AddOrderTransitions +
        //      AddUniqueOnSagaTransitions + OutboundIndexAudit; saga_state
        //      + outbound_saga_transitions land.
        //   3. OwnerSeed.SeedAsync(TenantConnectionString) — inserts the
        //      Owner row (Argon2id-hashed Email/Password) so the Sprint-11
        //      U1-extended RolePermissionsSeed's INSERTs land cleanly.
        //   4. RolePermissionsSeed.SeedAsync(TenantConnectionString) —
        //      Sprint-9 U12 + Sprint-11 U1 — inserts Owner row (24 perm
        //      keys via PermissionKeys.All) AND Picker row (4-key baseline
        //      from RolePermissionsSeed.PickerBaseline:
        //      OutboundOrdersRead + OutboundOrdersPickConfirm +
        //      InventoryRead + HubConnect).
        //   5. Raw INSERT INTO users (id, email, password_hash, role, ...)
        //      VALUES (PickerUserId, PickerEmail, <argon2 hash>, 'Picker', ...);
        //      — picker user seeded directly (Path B skips POST /admin/users
        //      because Auth.Api isn't booted in this fixture).

        JwtBuilder = new NarrowedJwtBuilder(DevSecret, Issuer, Audience);

        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            // Development env keeps the OrdersController.SeedAsync guard
            // open (Sprint-10.5 U4 mirror) + matches the seed/login dev
            // surface our other Outbound integration harnesses use.
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
public sealed class PickerCollection : ICollectionFixture<PickerFixture>
{
    public const string Name = "Picker";
}
