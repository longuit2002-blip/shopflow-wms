using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using ShopFlow.Auth.Infrastructure;
using ShopFlow.ControlPlane.Domain;
using ShopFlow.ControlPlane.Infrastructure;
using ShopFlow.Migrate.Provisioning;
using Testcontainers.PostgreSql;

namespace ShopFlow.Auth.IntegrationTests;

/// <summary>
/// Finish-line U5 — the multi-tenant Auth WAF fixture that
/// <see cref="AuthCrossTenantTests"/> (and
/// <see cref="Authorization.CrossTenant403Test"/>) were always meant to
/// run against but which was never built (the <c>MultiTenantAuthFixture</c>
/// named in CLAUDE.md). It is the HEAVY part of the tenant-isolation
/// hard-problem proof (AE3 / origin R32).
///
/// <para>Boots <c>Auth.Api</c> in-process via
/// <see cref="WebApplicationFactory{TEntryPoint}"/> against a single
/// Testcontainers Postgres instance, and provisions:</para>
/// <list type="bullet">
///   <item><description>one control-plane catalog DB (migrated), with
///     two tenant rows registered + marked <c>Ready</c> so the live
///     <c>TenantRoutingMiddleware</c> resolves them;</description></item>
///   <item><description>two per-tenant Auth DBs (<c>tenant-a</c> +
///     <c>tenant-b</c>), each migrated, each seeded with the canonical
///     <c>RolePermissionsSeed</c> baselines and a single distinct Owner
///     user row (distinct email + captured id) so cross-tenant data
///     leakage is observable.</description></item>
/// </list>
///
/// <para>Modelled on <c>Authorization.AuthAdminAuthorizationFixture</c>
/// (the proven Auth.Api WAF boot — Production env + ForwardedHeaders
/// allowlist + Auth secrets + InMemory bus) and
/// <c>Outbound.IntegrationTests.Handoff.HandoffFixture</c> (the
/// control-plane catalog provisioning + <c>Tenant.Create</c> →
/// <c>BeginProvisioning</c> → <c>MarkProvisioned</c> registration).</para>
///
/// <para>The tests are claim-based: <see cref="NarrowedJwtBuilder"/>
/// mints the <c>perm[]</c> + <c>tenant_slug</c> directly, so no
/// <c>/login</c> round-trip (and thus no password verification) is
/// needed — the seeded Owner rows exist only so the per-tenant
/// <c>users</c> / <c>role_permissions</c> tables have observable,
/// tenant-distinct contents.</para>
///
/// <para>Gated by <c>ProofGate</c> (finish-line U1): the suite runs only
/// when <c>SHOPFLOW_RUN_PROOFS</c> is set (locally via <c>task proofs</c>)
/// or on CI; a plain <c>dotnet test</c> skips it cleanly.</para>
/// </summary>
public sealed class MultiTenantAuthFixture : IAsyncLifetime
{
    public const string DevSecret = "shopflow-dev-only-do-not-use-in-prod-32bytes!!";
    public const string Issuer = "shopflow-dev";
    public const string Audience = "shopflow-api";

    public const string TenantASlug = "tenant-a";
    public const string TenantBSlug = "tenant-b";

    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder()
        .WithImage("postgres:16")
        .WithDatabase("postgres")
        .WithUsername("postgres")
        .WithPassword("postgres")
        .Build();

    private WebApplicationFactory<Program>? _factory;
    private string _adminConnectionString = string.Empty;

    public Authorization.NarrowedJwtBuilder JwtBuilder { get; private set; } = default!;

    /// <summary>tenant-a's provisioning record (slug, connection string, seeded Owner id + email).</summary>
    public ProvisionedTenant TenantA { get; private set; } = default!;

    /// <summary>tenant-b's provisioning record (slug, connection string, seeded Owner id + email).</summary>
    public ProvisionedTenant TenantB { get; private set; } = default!;

    public WebApplicationFactory<Program> Factory =>
        _factory ?? throw new InvalidOperationException("Fixture not initialized.");

    /// <summary>A fresh <see cref="HttpClient"/> per call so per-test
    /// header mutation (Authorization + <c>X-ShopFlow-Tenant</c>) never
    /// leaks between tests sharing this collection fixture.</summary>
    public HttpClient CreateClient() => Factory.CreateClient();

    public async Task InitializeAsync()
    {
        await _container.StartAsync();
        _adminConnectionString = _container.GetConnectionString();

        // ── Control-plane catalog ────────────────────────────────────────
        var controlConnStr = await CreateAndMigrateControlPlaneDbAsync();

        // ── Two tenants: create + migrate Auth DB, seed, register ────────
        TenantA = await ProvisionTenantAsync(controlConnStr, TenantASlug);
        TenantB = await ProvisionTenantAsync(controlConnStr, TenantBSlug);

        JwtBuilder = new Authorization.NarrowedJwtBuilder(DevSecret, Issuer, Audience);

        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            // Mirror AuthAdminAuthorizationFixture: boot in Production (no dev
            // relaxations masking the tenant boundary), satisfy the Sprint-9
            // KTD7 ForwardedHeaders guard via the config key it actually reads,
            // and wire the same Auth secrets the NarrowedJwtBuilder signs with.
            b.UseSetting("Auth:ForwardedHeaders:KnownNetworks:0", "127.0.0.0/8");
            b.UseSetting("Auth:DevSecret", DevSecret);
            b.UseSetting("Auth:Issuer", Issuer);
            b.UseSetting("Auth:Audience", Audience);
            b.UseSetting("ConnectionStrings:Redis", "localhost:6379");
            b.UseSetting("MessageBus:Transport", "InMemory");
            b.UseSetting("ControlPlane:ConnectionString", controlConnStr);
            b.UseSetting("ControlPlane:TenantTemplate", BuildTenantTemplate());
        });

        // Force the IHostedService startup chain (InMemory bus) to run before
        // the first request lands — same warm-up as HandoffFixture.
        _ = Factory.CreateClient();
        await Task.Yield();
    }

    public async Task DisposeAsync()
    {
        if (_factory is not null)
        {
            await _factory.DisposeAsync();
        }
        await _container.DisposeAsync();
    }

    // ── Provisioning helpers ─────────────────────────────────────────────

    private async Task<string> CreateAndMigrateControlPlaneDbAsync()
    {
        var dbName = $"shopflow_control_{Guid.NewGuid().ToString("N")[..8]}";
        await CreateDatabaseAsync(dbName);
        var connStr = ConnStrFor(dbName);
        var options = new DbContextOptionsBuilder<ControlPlaneDbContext>()
            .UseNpgsql(connStr, npg => npg.MigrationsAssembly("ShopFlow.ControlPlane.Migrations"))
            .Options;
        await using var ctx = new ControlPlaneDbContext(options);
        await ctx.Database.MigrateAsync();
        return connStr;
    }

    private async Task<ProvisionedTenant> ProvisionTenantAsync(string controlConnStr, string slug)
    {
        // 1. Create + migrate the per-tenant Auth DB.
        var dbName = $"shopflow_auth_{slug.Replace('-', '_')}_{Guid.NewGuid().ToString("N")[..8]}";
        await CreateDatabaseAsync(dbName);
        var connStr = ConnStrFor(dbName);

        var options = new DbContextOptionsBuilder<AuthDbContext>()
            .UseNpgsql(connStr, npg => npg.MigrationsAssembly("ShopFlow.Auth.Infrastructure"))
            .Options;
        await using (var ctx = new AuthDbContext(options))
        {
            await ctx.Database.MigrateAsync();
        }

        // 2. Seed the canonical role_permissions baselines (Owner/Picker/
        //    Dispatcher/Packer) — the production seed, so R32c has a known
        //    per-tenant starting state to diff against.
        var seed = new RolePermissionsSeed(NullLogger<RolePermissionsSeed>.Instance);
        await seed.SeedAsync(connStr, CancellationToken.None);

        // 3. Insert one distinct Owner user (distinct email + captured id) so
        //    user-list isolation (R32d) and the foreign-target MFA-reset
        //    (R32e) are observable. Raw INSERT mirrors OwnerSeed's column
        //    shape; the password hash is irrelevant (claim-based tests never
        //    hit /login).
        var ownerId = Guid.NewGuid();
        var ownerEmail = $"owner@{slug}.test";
        await InsertOwnerUserAsync(connStr, ownerId, ownerEmail);

        // 4. Register the tenant in the catalog (Pending → Provisioning →
        //    Ready) so TenantRoutingMiddleware resolves the slug to this DB.
        await RegisterTenantInCatalogAsync(controlConnStr, slug, dbName);

        return new ProvisionedTenant(slug, connStr, ownerId, ownerEmail);
    }

    private async Task CreateDatabaseAsync(string dbName)
    {
        await using var conn = new NpgsqlConnection(_adminConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"CREATE DATABASE \"{dbName}\"";
        await cmd.ExecuteNonQueryAsync();
    }

    private string ConnStrFor(string dbName) =>
        new NpgsqlConnectionStringBuilder(_adminConnectionString)
        {
            Database = dbName,
        }.ConnectionString;

    private static async Task InsertOwnerUserAsync(string connStr, Guid id, string email)
    {
        await using var conn = new NpgsqlConnection(connStr);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        // Mirror OwnerSeed's INSERT column set. The hash is a syntactically
        // plausible Argon2id PHC placeholder — never verified (no /login).
        cmd.CommandText =
            "INSERT INTO users (id, email, password_hash, role, is_active, created_at, "
            + "failed_login_count, mfa_required, mfa_enrolled) "
            + "VALUES (@id, @email, @hash, 'Owner', true, NOW(), 0, true, false);";
        cmd.Parameters.AddWithValue("id", id);
        cmd.Parameters.AddWithValue("email", email.ToLowerInvariant());
        cmd.Parameters.AddWithValue(
            "hash",
            "$argon2id$v=19$m=19456,t=2,p=1$c2hvcGZsb3ctc2VlZA$c2hvcGZsb3ctcGxhY2Vob2xkZXItaGFzaC1ub3QtdmVyaWZpZWQ"
        );
        await cmd.ExecuteNonQueryAsync();
    }

    private async Task RegisterTenantInCatalogAsync(
        string controlConnStr,
        string slug,
        string dbName
    )
    {
        var options = new DbContextOptionsBuilder<ControlPlaneDbContext>()
            .UseNpgsql(
                controlConnStr,
                npg => npg.MigrationsAssembly("ShopFlow.ControlPlane.Migrations")
            )
            .Options;
        await using var ctx = new ControlPlaneDbContext(options);

        var create = Tenant.Create(
            slug: slug,
            dbName: dbName,
            region: "ap-southeast-1",
            tier: "free"
        );
        if (!create.IsSuccess)
        {
            throw new InvalidOperationException(
                $"failed to create tenant '{slug}' in catalog: {create.Error}"
            );
        }
        var entity = create.Value!;
        entity.BeginProvisioning();
        entity.MarkProvisioned();
        ctx.Tenants.Add(entity);
        await ctx.SaveChangesAsync();
    }

    private string BuildTenantTemplate()
    {
        // AddControlPlane requires the literal '{db}' token;
        // NpgsqlConnectionStringBuilder URL-encodes the braces, so un-escape
        // them (same pattern as AuthAdminAuthorizationFixture +
        // StockSyncHappyPathTests.BuildTenantTemplate).
        return new NpgsqlConnectionStringBuilder(_adminConnectionString) { Database = "{db}" }
            .ConnectionString.Replace("%7B", "{", StringComparison.OrdinalIgnoreCase)
            .Replace("%7D", "}", StringComparison.OrdinalIgnoreCase);
    }
}

/// <summary>One provisioned tenant: slug, the tenant DB connection string
/// (for direct cross-tenant verification reads), and the seeded Owner's id +
/// email.</summary>
public sealed record ProvisionedTenant(
    string Slug,
    string ConnectionString,
    Guid OwnerUserId,
    string OwnerEmail
);

[CollectionDefinition(Name)]
public sealed class MultiTenantAuthCollection : ICollectionFixture<MultiTenantAuthFixture>
{
    public const string Name = "MultiTenantAuth";
}
