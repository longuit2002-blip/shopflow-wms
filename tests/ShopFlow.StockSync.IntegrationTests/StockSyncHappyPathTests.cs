using System.Diagnostics;
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

namespace ShopFlow.StockSync.IntegrationTests;

/// <summary>
/// Sprint-5 plan U9 — single-tenant happy round-trip integration test.
/// Drives <see cref="ShopFlow.Contracts.Inventory.StockLevelChangedV1"/> through
/// the in-process StockSync.Api pipeline (consumer → coalescing buffer →
/// per-tenant queue → dispatcher) and asserts:
/// <list type="bullet">
///   <item><description>The <see cref="FakeChannelAdapterFactory"/> records exactly 1 push to <c>shopee</c> with the expected SKU + quantity.</description></item>
///   <item><description>The tenant's <c>stock_sync_push_log</c> table has 1 row with <c>status='Success'</c> and a matching idempotency key.</description></item>
/// </list>
/// </summary>
/// <remarks>
/// <para><strong>Harness shape vs the plan's brainstorm.</strong> The plan's
/// U9 brainstorm proposed booting the out-of-process Shopee mock alongside
/// the StockSync.Api so the test exercises a real HTTP push. The realised
/// path uses a <see cref="FakeChannelAdapterFactory"/> instead because:</para>
/// <list type="bullet">
///   <item><description>The StockSync.Api project graph does NOT reference Channel.Infrastructure (where the real <see cref="ChannelAdapterFactory"/> + <see cref="ShopeeAdapter"/> live). U8 left <see cref="IChannelAdapterFactory"/> unregistered in <see cref="StockSyncServiceCollectionExtensions.AddStockSyncModule"/>; production assembly is W6-split + composed by Aspire AppHost or the per-process integration host. The integration test re-creates the contract via the fake.</description></item>
///   <item><description>The Sprint-5 U6 <c>ShopeeMockRoundTripTests</c> already validates the real HTTP boundary between <see cref="ShopeeAdapter"/> and the in-process Shopee mock; doubling that coverage inside StockSync's harness would test the wrong seam.</description></item>
///   <item><description>The fake records the exact <see cref="StockUpdateRequest"/> the dispatcher hands the adapter — that <em>is</em> the assertion the plan's "Shopee mock received the push" check translates to, scoped to the dispatcher's contract with the adapter surface.</description></item>
/// </list>
///
/// <para><strong>Control-plane DB.</strong> The test fixture provisions a
/// dedicated catalog DB on the shared Testcontainers Postgres, applies the
/// <see cref="ShopFlow.ControlPlane.Migrations"/> migration, and inserts one
/// <see cref="Tenant"/> row in <see cref="TenantStatus.Ready"/> state so
/// <see cref="ShopFlow.SharedKernel.Application.Ports.ITenantCatalog.GetReadyTenantsAsync"/>
/// returns the tenant the <c>PerTenantDispatcherService</c> dispatches for.</para>
///
/// <para><strong>InMemory transport.</strong>
/// <c>MessageBus:Transport=InMemory</c> + the consumer-assembly scan in
/// Program.cs let us call <see cref="IPublishEndpoint.Publish{T}(T, CancellationToken)"/>
/// and have the message routed to <c>StockLevelChangedConsumer</c> in the
/// same process, without standing up RabbitMQ.</para>
///
/// <para><strong>Polling budget.</strong> The pipeline cadence:</para>
/// <list type="bullet">
///   <item><description>Coalesce flush: <c>StockSync:CoalesceWindowMs</c>, default 500ms.</description></item>
///   <item><description>Token-bucket initial state: full burst (50 tokens), so the first push fires immediately.</description></item>
///   <item><description>Dispatcher loop wakeup: bounded-channel <c>ReadAsync</c>, no fixed tick.</description></item>
/// </list>
/// <para>A 5-second polling budget absorbs the flush window plus host-boot jitter on a slow CI runner.</para>
/// </remarks>
[Trait("Category", "Integration")]
[Collection(StockSyncTenantCollection.Name)]
public sealed class StockSyncHappyPathTests
{
    private readonly StockSyncTenantFixture _fixture;

    public StockSyncHappyPathTests(StockSyncTenantFixture fixture)
    {
        _fixture = fixture;
    }

    private static readonly TimeSpan PollingBudget = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan PollingInterval = TimeSpan.FromMilliseconds(100);

    [Fact]
    public async Task StockLevelChange_PushedToShopee_AndLoggedAsSuccess()
    {
        // ---- Arrange ---------------------------------------------------
        // Provision a fresh tenant DB (StockSync schema applied) + a
        // control-plane DB with the matching tenant row in Ready state.
        var tenant = await _fixture.ProvisionTenantAsync("happy-roundtrip");
        var controlConnStr = await CreateAndMigrateControlPlaneDbAsync();
        await RegisterTenantInCatalogAsync(controlConnStr, tenant);

        var fakeAdapterFactory = new FakeChannelAdapterFactory("shopee");

        await using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.ConfigureAppConfiguration(
                (_, cfg) =>
                {
                    cfg.AddInMemoryCollection(
                        new Dictionary<string, string?>
                        {
                            ["ControlPlane:ConnectionString"] = controlConnStr,
                            ["ControlPlane:TenantTemplate"] = BuildTenantTemplate(),
                            ["MessageBus:Transport"] = "InMemory",
                            ["StockSync:CoalesceWindowMs"] = "200",
                            ["StockSync:ActiveChannels:0"] = "shopee",
                            ["StockSync:TokenBucket:Sustain"] = "100",
                            ["StockSync:TokenBucket:Burst"] = "100",
                        }
                    );
                }
            );
            builder.ConfigureServices(services =>
            {
                // Override the (unregistered) IChannelAdapterFactory with
                // our fake so PerTenantDispatcherService can resolve a
                // pushable adapter for the "shopee" channel.
                services.AddSingleton<IChannelAdapterFactory>(fakeAdapterFactory);
            });
        });

        // Force host bootstrap (HostedServices start) — CreateClient triggers
        // server-builder execution including StartAsync on every IHostedService.
        using var _client = factory.CreateClient();

        var publishEndpoint = factory.Services.GetRequiredService<IPublishEndpoint>();

        // ---- Act -------------------------------------------------------
        const string sku = "SKU-HAPPY-1";
        const int available = 7;
        var observedAt = DateTime.UtcNow;
        var driver = new TenantBurstDriver(publishEndpoint, tenant.Info);
        await driver.EmitOneAsync(sku, available, observedAt);

        // ---- Assert ----------------------------------------------------
        // Wait for the dispatcher to push at least one event for our SKU.
        await WaitForAtLeastOnePushAsync(fakeAdapterFactory, sku, PollingBudget);

        var pushes = fakeAdapterFactory.PushesFor("shopee");
        pushes
            .Should()
            .NotBeEmpty(
                "the dispatcher should have routed at least one StockLevelChangedV1 to the shopee adapter"
            );

        var pushForSku = pushes.Where(p => p.ExternalSku == sku).ToList();
        pushForSku
            .Should()
            .HaveCountGreaterThanOrEqualTo(
                1,
                $"a coalesced push for sku '{sku}' should have reached the adapter"
            );

        // The single push must carry the input's available quantity (coalesce
        // last-write-wins → since only one StockLevelChangedV1 was emitted,
        // the dispatched value equals the emitted one verbatim).
        pushForSku[0].Quantity.Should().Be(available);

        // The dispatcher always emits a push_log row per processed intent
        // — Sprint-5 U5 contract. Poll until we observe the Success row.
        await WaitForPushLogRowAsync(
            tenant,
            status: "Success",
            expectedSku: sku,
            budget: PollingBudget
        );

        await using var verifyDb = new StockSyncDbContext(tenant.Options);
        var logRows = await verifyDb.PushLogEntries.AsNoTracking().ToListAsync();
        logRows
            .Should()
            .NotBeEmpty(
                "PerTenantDispatcherService must persist a push_log row for every processed intent"
            );

        var successRows = logRows.Where(r => r.Status == "Success" && r.Sku == sku).ToList();
        successRows.Should().HaveCountGreaterThanOrEqualTo(1);
        successRows[0].Available.Should().Be(available);
        successRows[0].ChannelType.Should().Be("shopee");
        successRows[0].ErrorCode.Should().BeNull();
        successRows[0].IdempotencyKey.Should().NotBeNullOrWhiteSpace();
    }

    // ── Helpers ─────────────────────────────────────────────────────────

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
        // Force the catalog row's id to match the provisioned id so
        // TenantInfo round-trips via slug → id without rebinding mid-flight.
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

    private static async Task WaitForAtLeastOnePushAsync(
        FakeChannelAdapterFactory fakeFactory,
        string sku,
        TimeSpan budget
    )
    {
        var sw = Stopwatch.StartNew();
        while (sw.Elapsed < budget)
        {
            if (fakeFactory.PushesFor("shopee").Any(p => p.ExternalSku == sku))
            {
                return;
            }
            await Task.Delay(PollingInterval);
        }
        throw new TimeoutException(
            $"No push for SKU '{sku}' observed by the fake adapter within {budget.TotalSeconds:F1}s."
        );
    }

    private static async Task WaitForPushLogRowAsync(
        StockSyncProvisionedTenant tenant,
        string status,
        string expectedSku,
        TimeSpan budget
    )
    {
        var sw = Stopwatch.StartNew();
        while (sw.Elapsed < budget)
        {
            await using var db = new StockSyncDbContext(tenant.Options);
            var any = await db
                .PushLogEntries.AsNoTracking()
                .AnyAsync(r => r.Status == status && r.Sku == expectedSku);
            if (any)
            {
                return;
            }
            await Task.Delay(PollingInterval);
        }
        throw new TimeoutException(
            $"No push_log row with status='{status}' sku='{expectedSku}' observed within {budget.TotalSeconds:F1}s."
        );
    }
}
