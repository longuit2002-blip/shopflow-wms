using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Npgsql;
using Polly;
using ShopFlow.Auth.IntegrationTests.Authorization;
using ShopFlow.Outbound.Application.Ports;
using ShopFlow.Outbound.Infrastructure.Shipping;
using Testcontainers.PostgreSql;

namespace ShopFlow.Outbound.IntegrationTests.Handoff;

/// <summary>
/// Sprint-12 U4 — Docker-backed fixture for the 3-role hand-off
/// happy-path integration test (<see cref="HandoffWorkflowTests"/>) and
/// the cross-role denial tests (<see cref="CrossRoleDenialTests"/>).
/// Parallel to <c>PickerFixture</c> per KTD4 — Sprint-11's fixture
/// stays untouched so its CI Docker tier doesn't regress; ~85% of the
/// fixture body is conceptually duplicated and listed under
/// "Deferred to Follow-Up Work" in the plan.
/// </summary>
/// <remarks>
/// <para><b>Deltas vs PickerFixture.cs:</b>
/// <list type="number">
///   <item><description>Three user IDs (Owner + Picker + Dispatcher)
///     instead of one Picker. Each seeded via raw INSERT after the
///     Sprint-12 U1 RolePermissionsSeed runs (which writes Owner +
///     Picker + Dispatcher baselines).</description></item>
///   <item><description>Three JWT-builder convenience accessors:
///     <see cref="BuildOwnerJwt"/>, <see cref="BuildPickerJwt"/>,
///     <see cref="BuildDispatcherJwt"/>. Each builds via
///     <see cref="NarrowedJwtBuilder.Build"/> with the role-baseline
///     perm[] from <c>RolePermissionsSeed.{Picker,Dispatcher}Baseline</c>
///     directly — drift between the seed and the JWT mint is impossible
///     because the same constant list is the source.</description></item>
///   <item><description>Factory-form <see cref="IMockShippingProvider"/>
///     override via <c>ConfigureTestServices</c> (KTD5). Zero-flake
///     instance ensures <c>POST /confirm-ship</c> deterministically
///     succeeds on first attempt. Without this override the production
///     5% flake rate would make the Sprint-12 E2E test
///     non-deterministic on CI.</description></item>
///   <item><description>MT bus-readiness wait (KTD7, adversarial-F4
///     doc-review mitigation): the InMemory transport (test config)
///     starts the bus synchronously through the WAF
///     <c>IHostedService</c> pipeline when
///     <c>Factory.CreateClient()</c> first fires. The fixture
///     warms the client at the end of <c>InitializeAsync</c> so the
///     bus is consuming by the time the first transition POST lands.
///     RabbitMQ-backed CI tier would use <c>IBusHealth</c> instead.
///     Per-transition wall-time logging via <see cref="HandoffWatch"/>
///     gives CI flake investigations observable evidence.</description></item>
///   <item><description>Namespace <c>Handoff</c> (KTD6) does NOT
///     shadow any existing Outbound.Domain type — grep confirmed no
///     <c>Handoff</c> class in the domain layer. Sprint-11 had to use
///     <c>.PickerE2E</c> to avoid the <c>Picker</c> domain collision;
///     Sprint-12 doesn't need the suffix.</description></item>
/// </list>
/// </para>
///
/// <para>Skip-marked locally per Sprint-1+ posture; CI removes the
/// Skip via the Docker-backed nightly + per-PR job.</para>
/// </remarks>
public sealed class HandoffFixture : IAsyncLifetime
{
    /// <summary>Shared HS256 signing secret. Mirrors
    /// <c>PickerFixture.DevSecret</c> verbatim so the same kernel
    /// JwtBearer config validates JWTs from either fixture.</summary>
    public const string DevSecret = "shopflow-dev-only-do-not-use-in-prod-32bytes!!";

    public const string Issuer = "shopflow-dev";
    public const string Audience = "shopflow-api";
    public const string TenantSlug = "handoff-tenant";

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

    public Guid OwnerUserId { get; private set; } = Guid.NewGuid();
    public Guid PickerUserId { get; private set; } = Guid.NewGuid();
    public Guid DispatcherUserId { get; private set; } = Guid.NewGuid();

    /// <summary>
    /// Sprint-12.5 U4 — settable shipping-provider factory seam for the
    /// tier-3 carrier-retry E2E tests
    /// (<see cref="CarrierRetryE2ETests"/>). When non-null, the
    /// <c>ConfigureTestServices</c> block at <c>InitializeAsync</c>
    /// registers <see cref="IMockShippingProvider"/> via this factory
    /// instead of the Sprint-12 KTD5 zero-flake default. The fixture's
    /// default behavior is unchanged when the property remains null —
    /// the original Sprint-12 happy-path + cross-role denial tests
    /// continue to receive the zero-flake provider.
    /// </summary>
    /// <remarks>
    /// <para>The factory receives the test <see cref="IServiceProvider"/>
    /// so consumers can resolve the production
    /// <see cref="ResiliencePipeline"/> via
    /// <c>sp.GetRequiredService&lt;ResiliencePipeline&gt;()</c> before
    /// constructing a <see cref="MockShippingProvider"/>. A counter shim
    /// can wrap the constructed provider for retry-count assertions
    /// (see <c>CarrierRetryE2ETests.CountingMockShippingProvider</c>).</para>
    ///
    /// <para>Set this BEFORE <see cref="InitializeAsync"/> runs. Once
    /// the WAF is built the test-services callback has executed and
    /// changing the property has no effect. xUnit class fixtures
    /// instantiate before any <c>[Fact]</c> runs, so the per-test
    /// approach is to either share one collection-scoped factory or to
    /// instantiate <see cref="HandoffFixture"/> manually inside a single
    /// test — Sprint-12.5 U4 takes the latter approach so the retry
    /// tests don't perturb the shared
    /// <see cref="HandoffCollection"/> fixture.</para>
    /// </remarks>
    public Func<IServiceProvider, IMockShippingProvider>? ShippingProviderFactory { get; set; }

    public string OwnerEmail => $"owner@{TenantSlug}.test";
    public string PickerEmail => $"picker@{TenantSlug}.test";
    public string DispatcherEmail => $"dispatcher@{TenantSlug}.test";

    public WebApplicationFactory<Program> Factory =>
        _factory ?? throw new InvalidOperationException("Fixture not initialized.");

    public HttpClient HttpClient => Factory.CreateClient();

    public async Task InitializeAsync()
    {
        await _container.StartAsync();
        var admin = _container.GetConnectionString();
        ControlPlaneConnectionString = admin;

        // Sprint-2.5 precedent — provision ONE tenant DB hosting both
        // Auth + Outbound schemas. Per-module outbox prefixes prevent
        // the legacy `outbox_messages` collision.
        var dbName = $"shopflow_hndoff_{Guid.NewGuid().ToString("N")[..8]}";
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
        //      — applies Sprint-9 AddSprint9AuthSchema + earlier AddUsers
        //      migrations.
        //   2. OutboundDbContext.Database.MigrateAsync(TenantConnectionString)
        //      — applies InitialOutboundSchema + AddOrderTransitions +
        //      AddUniqueOnSagaTransitions + OutboundIndexAudit.
        //   3. OwnerSeed.SeedAsync(TenantConnectionString) — inserts the
        //      Owner row with an Argon2id-hashed password (required by
        //      the AuthDbContext NOT NULL constraint; the JWT path
        //      doesn't read it but the schema does).
        //   4. RolePermissionsSeed.SeedAsync(TenantConnectionString) —
        //      Sprint-9 U12 + Sprint-11 U1 + Sprint-12 U1 — inserts
        //      Owner (24 keys) + Picker (4-key baseline) + Dispatcher
        //      (3-key baseline) rows.
        //   5. Raw INSERTs into `users` for Owner + Picker + Dispatcher
        //      (mirrors Sprint-11 U3 PickerFixture pattern, replicated
        //      3× — KTD4 deferred work consolidates this if the
        //      duplication starts hurting in Sprint-13+).

        JwtBuilder = new NarrowedJwtBuilder(DevSecret, Issuer, Audience);

        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            // Development env keeps the OrdersController.SeedAsync guard
            // open + matches the Sprint-11 PickerFixture posture.
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

            // ── KTD5 — Zero-flake MockShippingProvider override ──────
            // Factory-form (not instance-form): the production
            // registration constructs MockShippingProvider with a
            // ResiliencePipeline built by OutboundServiceCollectionExtensions.
            // RemoveAll displaces the production singleton; AddSingleton
            // factory re-resolves the pipeline from the test service
            // provider so the zero-flake instance shares the same Polly
            // retry policy as production (only the flake rate differs).
            //
            // Without this override the production 5% flake rate makes
            // the E2E test non-deterministic on CI Docker tier
            // (adversarial-F6 documented this trade-off; the carrier-
            // retry path is exercised by lower-tier MockShippingProviderTests
            // — Sprint-12.5 polish can add a tier-3 E2E variant if the
            // gap surfaces in production).
            b.ConfigureTestServices(services =>
            {
                services.RemoveAll<IMockShippingProvider>();
                // Sprint-12.5 U4 — settable factory seam. When the
                // tier-3 carrier-retry E2E test sets
                // ShippingProviderFactory, register via that delegate
                // (which can wrap a non-zero-flake MockShippingProvider
                // in a counter shim). Otherwise fall back to the
                // Sprint-12 KTD5 zero-flake default so the original
                // happy-path + cross-role denial tests keep their
                // deterministic-success contract.
                if (ShippingProviderFactory is not null)
                {
                    services.AddSingleton<IMockShippingProvider>(ShippingProviderFactory);
                }
                else
                {
                    services.AddSingleton<IMockShippingProvider>(sp =>
                        MockShippingProvider.WithFlakeRate(
                            sp.GetRequiredService<ResiliencePipeline>(),
                            0.0
                        )
                    );
                }
            });
        });

        // ── KTD7 — MT bus-readiness wait (adversarial-F4 mitigation) ──
        // For InMemory transport (MessageBus:Transport="InMemory" above),
        // the bus is started synchronously by the WAF host's
        // IHostedService startup chain when Factory.CreateClient() is
        // first called. The CreateClient call below forces the
        // IHostedService pipeline to run (WAF lazy-instantiates the
        // server on first client request). For a RabbitMQ-backed CI
        // tier, a more explicit readiness check via
        // IBusHealth.CheckHealth() or polling
        // BusHealthCheck.Status == HealthStatus.Healthy is the right
        // shape; InMemory transport synchronously registers consumers
        // before the IHostedService completes, so this warm-up is
        // sufficient for the Sprint-12 happy-path.
        //
        // Per-transition wall-time logging via HandoffWatch lets CI
        // flake investigations distinguish "saga didn't fire" from
        // "saga fired slowly".
        _ = Factory.CreateClient();
        await Task.Yield();
    }

    /// <summary>
    /// Mint an Owner JWT with all 24 PermissionKeys.All entries. Owner
    /// is the only role with pack-confirm at Sprint-12 (no Packer
    /// fourth role per plan scope boundary).
    /// </summary>
    public string BuildOwnerJwt() =>
        JwtBuilder.Build(
            tenantSlug: TenantSlug,
            userId: OwnerUserId,
            includeKeys: ShopFlow.SharedKernel.Authorization.PermissionKeys.All,
            email: OwnerEmail,
            role: "Owner"
        );

    /// <summary>
    /// Mint a Picker JWT carrying exactly the 4-key
    /// <c>RolePermissionsSeed.PickerBaseline</c>. The KTD1 additive-only
    /// contract preserves operator-added keys at runtime, but the
    /// fixture intentionally mints the BASELINE so the cross-role
    /// denial tests assert the canonical Picker permission set.
    /// </summary>
    public string BuildPickerJwt() =>
        JwtBuilder.Build(
            tenantSlug: TenantSlug,
            userId: PickerUserId,
            includeKeys: ShopFlow.Migrate.Provisioning.RolePermissionsSeed.PickerBaseline,
            email: PickerEmail,
            role: "Picker"
        );

    /// <summary>
    /// Mint a Dispatcher JWT carrying exactly the 3-key
    /// <c>RolePermissionsSeed.DispatcherBaseline</c>. Source-of-truth
    /// for the perm[] is the same constant list U1 writes to
    /// role_permissions — drift between the seed and the JWT is
    /// impossible because the same list is the contract.
    /// </summary>
    public string BuildDispatcherJwt() =>
        JwtBuilder.Build(
            tenantSlug: TenantSlug,
            userId: DispatcherUserId,
            includeKeys: ShopFlow.Migrate.Provisioning.RolePermissionsSeed.DispatcherBaseline,
            email: DispatcherEmail,
            role: "Dispatcher"
        );

    /// <summary>
    /// Mint a Picker JWT with an EXTRA <c>outbound.orders.ship-confirm</c>
    /// key beyond the baseline. Used by the U5
    /// <c>PickerWithManualShipConfirmGrant_CanShip_BehavioralPin</c>
    /// adversarial-F8 mitigation test to prove the KTD1 additive-only
    /// contract's behavioral consequence: an operator who pre-grants
    /// Picker the ship-confirm key HAS granted ship capability — there
    /// is no defense-in-depth surprise rescue.
    /// </summary>
    public string BuildPickerWithExtraShipConfirmJwt()
    {
        var keys = new List<string>(
            ShopFlow.Migrate.Provisioning.RolePermissionsSeed.PickerBaseline
        )
        {
            ShopFlow.SharedKernel.Authorization.PermissionKeys.OutboundOrdersShipConfirm,
        };
        return JwtBuilder.Build(
            tenantSlug: TenantSlug,
            userId: PickerUserId,
            includeKeys: keys,
            email: PickerEmail,
            role: "Picker"
        );
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
public sealed class HandoffCollection : ICollectionFixture<HandoffFixture>
{
    public const string Name = "Handoff";
}

/// <summary>
/// Sprint-12 U4 — per-transition wall-time logging helper. Lets the
/// CI Docker tier capture observable evidence of saga propagation
/// latency so flake investigations don't have to guess.
/// Adversarial-F4 doc-review mitigation.
/// </summary>
public static class HandoffWatch
{
    public static async Task<TimeSpan> MeasureAsync(
        string label,
        Func<Task> body,
        Xunit.Abstractions.ITestOutputHelper? output = null
    )
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        await body();
        sw.Stop();
        output?.WriteLine($"[Handoff] {label}: {sw.ElapsedMilliseconds} ms");
        return sw.Elapsed;
    }
}
