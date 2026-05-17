using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using ShopFlow.SharedKernel.Infrastructure;
using ShopFlow.StockSync.Application.Coalescing;
using ShopFlow.StockSync.Application.Dispatch;
using ShopFlow.StockSync.Application.Options;
using ShopFlow.StockSync.Application.Ports;
using ShopFlow.StockSync.Infrastructure;
using ShopFlow.StockSync.Infrastructure.Background;
using ShopFlow.StockSync.Infrastructure.Breaker;
using ShopFlow.StockSync.Infrastructure.Dispatch;
using ShopFlow.StockSync.Infrastructure.Pipeline;
using ShopFlow.StockSync.Infrastructure.RateLimit;

namespace ShopFlow.StockSync.IntegrationTests;

/// <summary>
/// Sprint-5 plan U8 — StockSync.Api composition smoke tests. Verifies that
/// the <c>AddShopFlowDefaults → AddControlPlane → AddStockSyncModule</c>
/// chain wires every required port + hosted service, and that the
/// diagnostics endpoint honors the <see cref="StockSyncOptions.DiagnosticsEnabled"/>
/// flag.
/// </summary>
/// <remarks>
/// <para>Tests boot the in-process <c>StockSync.Api</c> host through
/// <see cref="WebApplicationFactory{TEntryPoint}"/>. The fake control-plane
/// connection string only needs to be syntactically valid — the
/// <c>ControlPlaneDbContext</c> isn't opened at startup, and the per-tenant
/// dispatcher service catches the inevitable connection failure inside its
/// own try/catch (logs + returns without faulting the host).</para>
///
/// <para>MassTransit transport pinned to <c>InMemory</c> via the
/// <c>MessageBus:Transport</c> config override so no real RabbitMQ broker
/// is needed for these smoke tests. Same precedent Channel.IntegrationTests
/// uses.</para>
///
/// <para>Tagged <c>Category=Integration</c> so the default
/// <c>dotnet test</c> filter on dev machines skips it (Docker-less hosts
/// don't pay the Postgres bootstrap), while CI's per-PR integration job
/// runs the suite.</para>
/// </remarks>
[Trait("Category", "Integration")]
public sealed class StockSyncApiCompositionTests
{
    private const string FakeControlPlaneConn =
        "Host=localhost;Port=15432;Database=shopflow_control_test;Username=test;Password=test";

    private const string FakeTenantTemplate =
        "Host=localhost;Port=15432;Database={db};Username=test;Password=test";

    private static Dictionary<string, string?> BaseConfig(bool diagnosticsEnabled) =>
        new()
        {
            ["ControlPlane:ConnectionString"] = FakeControlPlaneConn,
            ["ControlPlane:TenantTemplate"] = FakeTenantTemplate,
            ["MessageBus:Transport"] = "InMemory",
            ["StockSync:DiagnosticsEnabled"] = diagnosticsEnabled ? "true" : "false",
        };

    [Fact]
    public void AddStockSyncModule_RegistersEveryRequiredPortAndHostedService()
    {
        using var factory = BuildFactory(diagnosticsEnabled: false);
        var services = factory.Services;

        // ---- Application ports + impls ----
        services.GetService<ICoalescingBuffer>().Should().NotBeNull();
        services.GetService<IPerTenantQueue>().Should().NotBeNull();
        services.GetService<ISkuFlagRepository>().Should().NotBeNull();
        services.GetService<IChannelLookupPort>().Should().NotBeNull();

        // ---- Registries + factories ----
        services.GetService<TenantChannelBucketRegistry>().Should().NotBeNull();
        services.GetService<TenantChannelBreakerRegistry>().Should().NotBeNull();
        services.GetService<PushPipelineFactory>().Should().NotBeNull();

        // ---- Options binding ----
        var opts = services.GetRequiredService<IOptions<StockSyncOptions>>().Value;
        opts.CoalesceWindowMs.Should().Be(500);
        opts.ActiveChannels.Should().ContainSingle().Which.Should().Be("shopee");
        opts.TokenBucket.Sustain.Should().Be(10);

        // ---- HostedServices ----
        var hostedTypes = services.GetServices<IHostedService>().Select(s => s.GetType()).ToList();
        hostedTypes.Should().Contain(typeof(CoalesceFlushService));
        hostedTypes.Should().Contain(typeof(PerTenantDispatcherService));
        hostedTypes.Should().Contain(typeof(MultiplexedOutboxDispatcher<StockSyncDbContext>));

        // ---- Outbox route registry seeded by AddOutboxRoute<StockLevelChangedV1> ----
        var routeRegistry = services.GetService<IOutboxRouteRegistry>();
        routeRegistry.Should().NotBeNull();
    }

    [Fact]
    public async Task GetSyncState_ReturnsNotFound_WhenDiagnosticsDisabled()
    {
        using var factory = BuildFactory(diagnosticsEnabled: false);
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(new Uri("/api/sync/state", UriKind.Relative));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GetSyncState_Returns200WithExpectedKeys_WhenDiagnosticsEnabled()
    {
        using var factory = BuildFactory(diagnosticsEnabled: true);
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(new Uri("/api/sync/state", UriKind.Relative));

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var bodyStream = await response.Content.ReadAsStreamAsync();
        using var doc = await JsonDocument.ParseAsync(bodyStream);
        var root = doc.RootElement;

        root.TryGetProperty("buffer", out var buffer).Should().BeTrue();
        buffer.TryGetProperty("count", out _).Should().BeTrue();

        root.TryGetProperty("options", out var options).Should().BeTrue();
        options.TryGetProperty("coalesceWindowMs", out _).Should().BeTrue();
        options.TryGetProperty("activeChannels", out _).Should().BeTrue();
        options.TryGetProperty("tokenBucket", out _).Should().BeTrue();
        options.TryGetProperty("breaker", out _).Should().BeTrue();

        root.TryGetProperty("timestamp", out _).Should().BeTrue();
    }

    private static WebApplicationFactory<Program> BuildFactory(bool diagnosticsEnabled)
    {
        return new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.ConfigureAppConfiguration((_, cfg) =>
            {
                cfg.AddInMemoryCollection(BaseConfig(diagnosticsEnabled));
            });
        });
    }
}
