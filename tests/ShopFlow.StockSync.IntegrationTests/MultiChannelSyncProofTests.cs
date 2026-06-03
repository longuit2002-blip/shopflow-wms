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
using ShopFlow.TestSupport;

namespace ShopFlow.StockSync.IntegrationTests;

/// <summary>
/// Finish-line U8 — the multi-channel sync proof (origin AE8 / R10). Makes the
/// "multi-channel WMS" headline real by demonstration, not assertion: with BOTH
/// Shopee and Lazada active, ONE stock change for a SKU fans out to a push on
/// EACH channel through the same coalescing → per-tenant queue → token-bucket →
/// breaker → adapter pipeline.
///
/// <para>This is the payoff of U7's second adapter. Per K4 / ADV-003 the engine
/// fans out to the global <c>ActiveChannels</c> set — there is no per-SKU channel
/// mapping, so the proof is "one change → both active channels," not
/// cross-channel allocation. Mirrors <see cref="StockSyncHappyPathTests"/>'s
/// harness (Testcontainers Postgres + control-plane catalog + InMemory transport
/// + <see cref="FakeChannelAdapterFactory"/> recorder); the only delta is two
/// active channels + a two-channel fake.</para>
///
/// <para>Gated behind <see cref="ProofFactAttribute"/> — run via <c>task proofs</c>
/// (or CI), skipped on a default no-Docker run.</para>
/// </summary>
[Trait("Category", "Integration")]
[Trait("Category", "Proof")]
[Collection(StockSyncTenantCollection.Name)]
public sealed class MultiChannelSyncProofTests
{
    private readonly StockSyncTenantFixture _fixture;

    public MultiChannelSyncProofTests(StockSyncTenantFixture fixture)
    {
        _fixture = fixture;
    }

    private static readonly TimeSpan PollingBudget = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan PollingInterval = TimeSpan.FromMilliseconds(100);

    [ProofFact]
    public async Task StockChange_WithShopeeAndLazadaActive_PushesToBothChannels()
    {
        // ---- Arrange — both channels active, two-channel recorder ----------
        var tenant = await _fixture.ProvisionTenantAsync("multi-channel");
        var controlConnStr = await CreateAndMigrateControlPlaneDbAsync();
        await RegisterTenantInCatalogAsync(controlConnStr, tenant);

        var fakeAdapterFactory = new FakeChannelAdapterFactory("shopee", "lazada");

        await using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            // Config via UseSetting (web-host-builder configuration), NOT
            // ConfigureAppConfiguration: under the minimal-hosting WAF the
            // ConfigureAppConfiguration callback lands too late for config read
            // during builder.Build() (AddControlPlane), so the appsettings-default
            // ControlPlane connection would win and the dispatcher would hang
            // connecting to an unreachable DB. UseSetting is the reliable surface
            // (this mirrors the U3 noisy-neighbor BuildHost pattern exactly).
            builder.UseSetting("ControlPlane:ConnectionString", controlConnStr);
            builder.UseSetting("ControlPlane:TenantTemplate", BuildTenantTemplate());
            builder.UseSetting("MessageBus:Transport", "InMemory");
            builder.UseSetting("Auth:ForwardedHeaders:KnownNetworks:0", "127.0.0.0/8");
            builder.UseSetting("StockSync:CoalesceWindowMs", "200");
            // BOTH marketplaces active — the engine fans out to every entry (K4).
            builder.UseSetting("StockSync:ActiveChannels:0", "shopee");
            builder.UseSetting("StockSync:ActiveChannels:1", "lazada");
            builder.UseSetting("StockSync:TokenBucket:Sustain", "100");
            builder.UseSetting("StockSync:TokenBucket:Burst", "100");
            builder.ConfigureServices(services =>
            {
                services.AddSingleton<IChannelAdapterFactory>(fakeAdapterFactory);
            });
        });

        using var _client = factory.CreateClient();
        // IPublishEndpoint is scoped; resolve the singleton IBus (which IS an
        // IPublishEndpoint) to publish from outside a consumer scope (matches U3).
        var publish = factory.Services.GetRequiredService<IBus>();

        // ---- Act — ONE stock change for a dual-listed SKU ------------------
        const string sku = "SKU-MULTI-1";
        const int available = 11;
        var driver = new TenantBurstDriver(publish, tenant.Info);
        await driver.EmitOneAsync(sku, available, DateTime.UtcNow);

        // ---- Assert — a push reached BOTH channels -------------------------
        await WaitForPushAsync(fakeAdapterFactory, "shopee", sku, PollingBudget);
        await WaitForPushAsync(fakeAdapterFactory, "lazada", sku, PollingBudget);

        var shopeePushes = fakeAdapterFactory
            .PushesFor("shopee")
            .Where(p => p.ExternalSku == sku)
            .ToList();
        var lazadaPushes = fakeAdapterFactory
            .PushesFor("lazada")
            .Where(p => p.ExternalSku == sku)
            .ToList();

        shopeePushes
            .Should()
            .HaveCountGreaterThanOrEqualTo(1, "the engine must fan the stock change out to Shopee");
        lazadaPushes
            .Should()
            .HaveCountGreaterThanOrEqualTo(1, "the engine must fan the stock change out to Lazada");

        // Both channels carry the same emitted quantity (single emit → coalesce
        // last-write-wins → dispatched value equals the input verbatim).
        shopeePushes[0].Quantity.Should().Be(available);
        lazadaPushes[0].Quantity.Should().Be(available);

        // ---- Assert — a Success push_log row per channel -------------------
        await WaitForPushLogRowAsync(tenant, "Success", sku, "shopee", PollingBudget);
        await WaitForPushLogRowAsync(tenant, "Success", sku, "lazada", PollingBudget);

        await using var verifyDb = new StockSyncDbContext(tenant.Options);
        var logRows = await verifyDb
            .PushLogEntries.AsNoTracking()
            .Where(r => r.Sku == sku)
            .ToListAsync();
        var channels = logRows
            .Where(r => r.Status == "Success")
            .Select(r => r.ChannelType)
            .Distinct()
            .ToList();
        channels
            .Should()
            .Contain(
                new[] { "shopee", "lazada" },
                "both active channels must log a successful push"
            );
    }

    // ── Helpers (mirror StockSyncHappyPathTests) ──────────────────────────

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

    private static async Task WaitForPushAsync(
        FakeChannelAdapterFactory fakeFactory,
        string channelType,
        string sku,
        TimeSpan budget
    )
    {
        var sw = Stopwatch.StartNew();
        while (sw.Elapsed < budget)
        {
            if (fakeFactory.PushesFor(channelType).Any(p => p.ExternalSku == sku))
            {
                return;
            }
            await Task.Delay(PollingInterval);
        }
        throw new TimeoutException(
            $"No push for SKU '{sku}' on channel '{channelType}' observed within {budget.TotalSeconds:F1}s."
        );
    }

    private static async Task WaitForPushLogRowAsync(
        StockSyncProvisionedTenant tenant,
        string status,
        string expectedSku,
        string channelType,
        TimeSpan budget
    )
    {
        var sw = Stopwatch.StartNew();
        while (sw.Elapsed < budget)
        {
            await using var db = new StockSyncDbContext(tenant.Options);
            var any = await db
                .PushLogEntries.AsNoTracking()
                .AnyAsync(r =>
                    r.Status == status && r.Sku == expectedSku && r.ChannelType == channelType
                );
            if (any)
            {
                return;
            }
            await Task.Delay(PollingInterval);
        }
        throw new TimeoutException(
            $"No push_log row status='{status}' sku='{expectedSku}' channel='{channelType}' within {budget.TotalSeconds:F1}s."
        );
    }
}
