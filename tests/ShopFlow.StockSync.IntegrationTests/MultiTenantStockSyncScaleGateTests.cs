using MassTransit;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using ShopFlow.Channel.Application.Adapters;
using ShopFlow.ControlPlane.Domain;
using ShopFlow.ControlPlane.Infrastructure;
using ShopFlow.StockSync.Infrastructure;
using ShopFlow.StockSync.IntegrationTests.Drivers;
using ShopFlow.TestSupport;
using Xunit.Abstractions;

namespace ShopFlow.StockSync.IntegrationTests;

/// <summary>
/// Sprint-5 plan U9 / finish-line U3 — multi-tenant noisy-neighbor scale gate
/// for the StockSync engine. One tenant floods the ingest path while four
/// peers emit a steady trickle; the proof is that the per-tenant coalescing
/// buffer + per-tenant priority queue + per-(tenant,channel) token bucket +
/// per-tenant dispatcher loop keep the peers flowing — A's flood stays in A's
/// lane.
/// </summary>
/// <remarks>
/// <para><strong>Finish-line U3 — written from the Sprint-5 empty stub.</strong>
/// Sprint-5 shipped this as two <c>[Fact(Skip)] { return Task.CompletedTask; }</c>
/// slots, deferring the harness to a "multi-tenant Aspire boot + real Shopee
/// mock" follow-up that never landed. This builds the noisy-neighbor proof on
/// the SAME harness the (real, passing) <see cref="StockSyncHappyPathTests"/>
/// uses — one <see cref="WebApplicationFactory{Program}"/> boot, a migrated
/// control-plane catalog with N tenants registered Ready, the InMemory bus,
/// and the <see cref="FakeChannelAdapterFactory"/> recorder as the downstream
/// (the dispatcher's contract surface; the real HTTP boundary is proven by
/// Sprint-5 U6's ShopeeMockRoundTripTests). No Aspire, no real mock needed for
/// the isolation property.</para>
///
/// <para><strong>Push→tenant attribution.</strong> <see cref="RecordedPush"/>
/// carries no tenant id (the dispatcher hands the adapter a
/// <c>StockUpdateRequest</c> with <c>ChannelId=Empty</c>), so each tenant
/// drives a DISTINCT sku and pushes are attributed back by sku.</para>
///
/// <para><strong>Why this is a correctness/isolation proof, not a benchmark.</strong>
/// The coalescing buffer collapses A's flood (thousands of changes on one sku)
/// to a bounded push count per coalesce window — so the downstream push counts
/// are similar across tenants by design. The load-bearing assertion is that
/// the peers are NOT starved (each records pushes, fairness ≥ floor) and their
/// end-to-end latency stays bounded under A's ingest flood. A regression that
/// broke per-tenant isolation (e.g., a shared blocking queue) would either
/// starve the peers or balloon their latency. Burst volume + wall-time are
/// bounded for CI per the plan.</para>
/// </remarks>
[Collection(StockSyncTenantCollection.Name)]
[Trait("Category", "Integration")]
[Trait("Category", "Load")]
[Trait("Category", "Proof")] // finish-line U3 — selectable via `task proofs`
public sealed class MultiTenantStockSyncScaleGateTests
{
    private const string Channel = "shopee";
    private const int VictimCount = 4; // tenants B, C, D, E
    private const double FairnessFloor = 0.85;

    // Bounded for CI: A floods ingest ~50× the peers' rate over a short window.
    // The ratio (not the absolute rate) is what exercises per-tenant isolation.
    private const int NoisyRatePerSecond = 1000;
    private const int VictimRatePerSecond = 20;
    private static readonly TimeSpan DriveDuration = TimeSpan.FromSeconds(5);

    // Generous SLO: the Sprint-5 R8 spec is p99 < 30s. Met trivially when
    // isolation holds; a shared-queue regression under A's flood would blow it.
    private const double VictimP99SloMs = 30_000;

    private static readonly TimeSpan DrainBudget = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan DrainQuiet = TimeSpan.FromSeconds(2);

    private readonly StockSyncTenantFixture _fixture;
    private readonly ITestOutputHelper _output;

    public MultiTenantStockSyncScaleGateTests(
        StockSyncTenantFixture fixture,
        ITestOutputHelper output
    )
    {
        _fixture = fixture;
        _output = output;
    }

    /// <summary>
    /// Sprint-5 R8 / AE2 — 5 tenants; tenant A floods the ingest path while the
    /// four peers emit a steady trickle. The peers are not starved (each pushes,
    /// fairness ≥ 0.85) and their end-to-end push latency stays under the SLO —
    /// proving A's flood stays in A's per-tenant lane.
    /// </summary>
    [ProofFact]
    public async Task NoisyNeighborBurst_TenantAFloodsIngest_PeersNotStarved_AndMeetLatencySlo()
    {
        // ── Arrange — provision 5 tenant DBs + a migrated catalog with all 5
        //    registered Ready BEFORE the host boots (the per-tenant dispatcher
        //    enumerates ready tenants once at startup). ────────────────────────
        var noisy = await _fixture.ProvisionTenantAsync("noisy-a");
        var victims = new List<StockSyncProvisionedTenant>(VictimCount);
        for (var i = 0; i < VictimCount; i++)
        {
            victims.Add(await _fixture.ProvisionTenantAsync($"victim-{(char)('b' + i)}"));
        }
        var allTenants = new List<StockSyncProvisionedTenant> { noisy };
        allTenants.AddRange(victims);

        var controlConnStr = await CreateAndMigrateControlPlaneDbAsync();
        foreach (var t in allTenants)
        {
            await RegisterTenantInCatalogAsync(controlConnStr, t);
        }

        var fake = new FakeChannelAdapterFactory(Channel);

        await using var factory = BuildHost(controlConnStr, fake);
        using var _client = factory.CreateClient(); // forces IHostedService startup
        // IPublishEndpoint is scoped; resolve the singleton IBus (which IS an
        // IPublishEndpoint) to publish from outside a consumer scope.
        var publish = factory.Services.GetRequiredService<IBus>();

        var noisySku = "SKU-FLASH-A";
        string VictimSku(StockSyncProvisionedTenant t) => $"SKU-{t.Info.Slug.ToUpperInvariant()}";

        // ── Act — drive all tenants concurrently for the same window. ──────────
        var noisyDriver = new TenantBurstDriver(publish, noisy.Info);
        var victimDrivers = victims
            .Select(t => (Tenant: t, Driver: new TenantBurstDriver(publish, t.Info)))
            .ToList();

        var drive = new List<Task>
        {
            noisyDriver.BurstAsync(noisySku, NoisyRatePerSecond, DriveDuration, parallelism: 8),
        };
        drive.AddRange(
            victimDrivers.Select(v =>
                v.Driver.ConstantAsync(VictimSku(v.Tenant), VictimRatePerSecond, DriveDuration)
            )
        );
        await Task.WhenAll(drive);

        // ── Drain — wait for the pipeline (coalesce flush → queue → bucket →
        //    dispatch) to settle: push count stops growing for DrainQuiet. ──────
        await WaitForDrainAsync(fake);

        var pushes = fake.PushesFor(Channel);

        // ── Assert — per-victim attribution by sku. ────────────────────────────
        var victimPushCounts = new Dictionary<string, double>();
        var victimLatencies = new List<double>();
        foreach (var (tenant, _) in victimDrivers)
        {
            var sku = VictimSku(tenant);
            var forSku = pushes.Where(p => p.ExternalSku == sku).ToList();
            victimPushCounts[tenant.Info.Slug] = forSku.Count;
            victimLatencies.AddRange(
                forSku.Select(p => (p.PushedAt - p.ObservedAt).TotalMilliseconds)
            );
            _output.WriteLine($"victim={tenant.Info.Slug} sku={sku} pushes={forSku.Count}");
        }

        var noisyPushes = pushes.Count(p => p.ExternalSku == noisySku);
        var victimP99 = FairnessCalculator.Percentile(victimLatencies, 99);
        var fairness = FairnessCalculator.FairnessFloor(victimPushCounts);
        _output.WriteLine(
            $"noisy sku={noisySku} pushes={noisyPushes} | victim p99={victimP99:F1}ms "
                + $"| victim-count-fairness={fairness:F3} | total pushes={pushes.Count}"
        );

        // 1. No starvation — every peer's pipeline ran despite A's flood.
        foreach (var (tenant, _) in victimDrivers)
        {
            victimPushCounts[tenant.Info.Slug]
                .Should()
                .BeGreaterThan(
                    0,
                    $"victim {tenant.Info.Slug} must get pushes through while tenant A floods ingest"
                );
        }

        // 2. The noisy tenant itself isn't broken (its flood coalesces to a
        //    bounded push count, but it does flow).
        noisyPushes.Should().BeGreaterThan(0, "the noisy tenant's coalesced pushes still flow");

        // 3. Fairness floor across the four peers — none crowded out by another.
        fairness
            .Should()
            .BeGreaterThanOrEqualTo(
                FairnessFloor,
                "per-tenant queues + buckets must treat the four peers equitably"
            );

        // 4. Bounded peer latency under A's flood — the isolation assertion.
        victimP99
            .Should()
            .BeLessThan(
                VictimP99SloMs,
                "tenant A's ingest flood must not back up the peers' dispatch latency"
            );

        NpgsqlConnection.ClearAllPools();
    }

    /// <summary>
    /// Sprint-5 R9 — per-tenant breaker isolation + recovery. DEFERRED, not a
    /// hollow stub: this assertion ("tenant A's breaker trips on channel
    /// failure while tenant B is unaffected, then A recovers after cooldown")
    /// requires PER-TENANT failure injection. The current
    /// <see cref="FakeChannelAdapter"/> fails per channel TYPE (its
    /// <c>FailWith</c> is keyed by "shopee"), and the dispatcher hands it a
    /// <c>StockUpdateRequest</c> with <c>ChannelId=Empty</c> — so the fake
    /// cannot fail one tenant's pushes while sparing another. The Sprint-5 stub
    /// flagged this same limitation ("process-wide chaos … per-tenant chaos is
    /// Phase-3"). Closing it cleanly means extending the fake to target a
    /// tenant (carry tenant id on the push request, or key FailWith by tenant),
    /// which is a harness change beyond U3's noisy-neighbor scope. The breaker
    /// primitives themselves are unit-proven in Sprint-5 U5
    /// (PushPipelineFactory + TenantChannelBreakerRegistry tests).
    /// </summary>
    [Fact(
        Skip = "finish-line: R9 per-tenant breaker isolation needs per-tenant failure injection in the "
            + "fake adapter (push request carries ChannelId=Empty, so the fake can't target one tenant). "
            + "Breaker primitives are unit-proven (Sprint-5 U5); harness extension deferred. AE2 "
            + "noisy-neighbor is the U3 proof."
    )]
    public Task BreakerRecovery_ChaosToggleOnTenantA_TripsThenRecovers_BUnaffected()
    {
        return Task.CompletedTask;
    }

    // ── Harness wiring (replicated from StockSyncHappyPathTests; intra-project
    //    duplication accepted, consolidation deferred). ──────────────────────────

    private WebApplicationFactory<Program> BuildHost(
        string controlConnStr,
        FakeChannelAdapterFactory fake
    ) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            // Config via UseSetting (web-host-builder configuration), NOT
            // ConfigureAppConfiguration. Under the minimal-hosting WAF, the
            // ConfigureAppConfiguration callback lands too late for the
            // composition-root reads (AddShopFlowDefaults' KTD7 guard +
            // AddControlPlane), which run during builder.Build(); UseSetting is
            // visible immediately. (The StockSyncHappyPathTests' AddInMemory
            // approach has the same latent gap — it had never run.)
            builder.UseSetting("ControlPlane:ConnectionString", controlConnStr);
            builder.UseSetting("ControlPlane:TenantTemplate", BuildTenantTemplate());
            builder.UseSetting("MessageBus:Transport", "InMemory");
            // Sprint-9 KTD7 guard reads this config key (not IWebHostEnvironment)
            // and throws in non-Development on an empty allowlist. Trust loopback.
            builder.UseSetting("Auth:ForwardedHeaders:KnownNetworks:0", "127.0.0.0/8");
            builder.UseSetting("StockSync:CoalesceWindowMs", "200");
            builder.UseSetting("StockSync:ActiveChannels:0", Channel);
            builder.UseSetting("StockSync:TokenBucket:Sustain", "100");
            builder.UseSetting("StockSync:TokenBucket:Burst", "100");
            builder.ConfigureServices(services =>
            {
                services.AddSingleton<IChannelAdapterFactory>(fake);
            });
        });

    private async Task<string> CreateAndMigrateControlPlaneDbAsync()
    {
        var dbName = $"shopflow_control_{Guid.NewGuid().ToString("N")[..8]}";

        await using (var admin = new NpgsqlConnection(_fixture.AdminConnectionString))
        {
            await admin.OpenAsync();
            await using var cmd = admin.CreateCommand();
            cmd.CommandText = $"CREATE DATABASE \"{dbName}\"";
            await cmd.ExecuteNonQueryAsync();
        }

        var connStr = new NpgsqlConnectionStringBuilder(_fixture.AdminConnectionString)
        {
            Database = dbName,
        }.ConnectionString;

        var options = new DbContextOptionsBuilder<ControlPlaneDbContext>()
            .UseNpgsql(connStr, npg => npg.MigrationsAssembly("ShopFlow.ControlPlane.Migrations"))
            .Options;
        await using var ctx = new ControlPlaneDbContext(options);
        await ctx.Database.MigrateAsync();
        return connStr;
    }

    private async Task RegisterTenantInCatalogAsync(
        string controlConnStr,
        StockSyncProvisionedTenant tenant
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
            slug: tenant.Info.Slug,
            dbName: tenant.Info.DbName,
            region: tenant.Info.Region,
            tier: tenant.Info.Tier
        );
        if (!create.IsSuccess)
        {
            throw new InvalidOperationException(
                $"failed to create tenant '{tenant.Info.Slug}' in catalog: {create.Error}"
            );
        }

        var entity = create.Value!;
        SetPrivateProperty(entity, "Id", tenant.Info.Id);
        entity.BeginProvisioning();
        entity.MarkProvisioned();
        ctx.Tenants.Add(entity);
        await ctx.SaveChangesAsync();
    }

    private string BuildTenantTemplate()
    {
        var template = new NpgsqlConnectionStringBuilder(_fixture.AdminConnectionString)
        {
            Database = "{db}",
            MaxPoolSize = 25,
        }.ConnectionString;
        return template
            .Replace("%7B", "{", StringComparison.OrdinalIgnoreCase)
            .Replace("%7D", "}", StringComparison.OrdinalIgnoreCase);
    }

    private static void SetPrivateProperty<T>(object instance, string propertyName, T value)
    {
        var prop =
            instance
                .GetType()
                .GetProperty(
                    propertyName,
                    System.Reflection.BindingFlags.Public
                        | System.Reflection.BindingFlags.NonPublic
                        | System.Reflection.BindingFlags.Instance
                )
            ?? throw new InvalidOperationException(
                $"property '{propertyName}' not found on {instance.GetType().FullName}."
            );
        prop.SetValue(instance, value);
    }

    private static async Task WaitForDrainAsync(FakeChannelAdapterFactory fake)
    {
        var deadline = DateTime.UtcNow + DrainBudget;
        var lastCount = -1;
        var stableSince = DateTime.UtcNow;
        while (DateTime.UtcNow < deadline)
        {
            var count = fake.PushesFor(Channel).Count;
            if (count != lastCount)
            {
                lastCount = count;
                stableSince = DateTime.UtcNow;
            }
            else if (count > 0 && DateTime.UtcNow - stableSince >= DrainQuiet)
            {
                return; // count held steady for the quiet window — drained
            }
            await Task.Delay(200);
        }
    }
}
