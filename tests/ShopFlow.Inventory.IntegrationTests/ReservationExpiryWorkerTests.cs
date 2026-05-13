using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Npgsql;
using ShopFlow.Inventory.Application;
using ShopFlow.Inventory.Application.Ports;
using ShopFlow.Inventory.Domain;
using ShopFlow.Inventory.Infrastructure;
using ShopFlow.Inventory.Infrastructure.Repositories;
using ShopFlow.Inventory.Infrastructure.Workers;
using ShopFlow.SharedKernel.Application;
using ShopFlow.SharedKernel.Application.Ports;
using ShopFlow.SharedKernel.Domain;

namespace ShopFlow.Inventory.IntegrationTests;

/// <summary>
/// Sprint-1-redux U3: <see cref="ReservationExpiryWorker"/> as a
/// multiplexed BackgroundService. Each test provisions one or two fresh
/// tenant DBs, seeds expired reservations directly via SQL (so the
/// real TTL clock doesn't need to tick), starts the worker, and asserts
/// the post-conditions per tenant.
/// </summary>
[Collection(InventoryTenantCollection.Name)]
[Trait("Category", "Integration")]
public sealed class ReservationExpiryWorkerTests
{
    private const string Sku = "SKU-EXP";

    private readonly InventoryTenantFixture _fx;

    public ReservationExpiryWorkerTests(InventoryTenantFixture fx)
    {
        _fx = fx;
    }

    [Fact]
    public void Constructor_NonPositiveInterval_Throws()
    {
        var bad = Options.Create(
            new InventoryOptions { ExpiryPollIntervalSeconds = 0, ExpiryBatchSize = 10 }
        );

        var act = () =>
            new ReservationExpiryWorker(
                scopeFactory: new EmptyScopeFactory(),
                options: bad,
                clock: TimeProvider.System,
                logger: NullLogger<ReservationExpiryWorker>.Instance
            );

        act.Should().Throw<ArgumentOutOfRangeException>().WithMessage("*ExpiryPollIntervalSeconds*");
    }

    [Fact]
    public void Constructor_NonPositiveBatchSize_Throws()
    {
        var bad = Options.Create(
            new InventoryOptions { ExpiryPollIntervalSeconds = 1, ExpiryBatchSize = 0 }
        );

        var act = () =>
            new ReservationExpiryWorker(
                scopeFactory: new EmptyScopeFactory(),
                options: bad,
                clock: TimeProvider.System,
                logger: NullLogger<ReservationExpiryWorker>.Instance
            );

        act.Should().Throw<ArgumentOutOfRangeException>().WithMessage("*ExpiryBatchSize*");
    }

    [Fact]
    public async Task SingleTenant_FirstTick_ReleasesExpiredReservations()
    {
        var tenant = await _fx.ProvisionTenantAsync("worker-1");
        await _fx.SeedStockAsync(tenant, Sku, available: 100);
        await SeedExpiredReservationAsync(tenant, "OLD-1", quantity: 7);
        await BumpReservedAsync(tenant, Sku, by: 7);

        var sp = BuildServiceProvider(intervalSeconds: 1, new[] { tenant });
        var worker = sp.GetRequiredService<ReservationExpiryWorker>();

        using var cts = new CancellationTokenSource();
        var execute = worker.StartAsync(cts.Token);

        await WaitForReleaseAsync(tenant, expectedExpiredCount: 1, timeoutMs: 5_000);
        cts.Cancel();
        await worker.StopAsync(CancellationToken.None);

        var rowCount = await CountExpiredAsync(tenant);
        rowCount.Should().Be(1);
    }

    [Fact]
    public async Task TwoTenants_OneTick_ReleasesExpiredInBoth_OutboxScopedPerTenant()
    {
        var tenantA = await _fx.ProvisionTenantAsync("worker-a");
        var tenantB = await _fx.ProvisionTenantAsync("worker-b");
        await _fx.SeedStockAsync(tenantA, Sku, available: 100);
        await _fx.SeedStockAsync(tenantB, Sku, available: 100);
        await SeedExpiredReservationAsync(tenantA, "OLD-A1", quantity: 4);
        await SeedExpiredReservationAsync(tenantA, "OLD-A2", quantity: 6);
        await SeedExpiredReservationAsync(tenantB, "OLD-B1", quantity: 9);
        await BumpReservedAsync(tenantA, Sku, by: 10);
        await BumpReservedAsync(tenantB, Sku, by: 9);

        var sp = BuildServiceProvider(intervalSeconds: 1, new[] { tenantA, tenantB });
        var worker = sp.GetRequiredService<ReservationExpiryWorker>();

        using var cts = new CancellationTokenSource();
        await worker.StartAsync(cts.Token);

        await WaitForReleaseAsync(tenantA, expectedExpiredCount: 2, timeoutMs: 5_000);
        await WaitForReleaseAsync(tenantB, expectedExpiredCount: 1, timeoutMs: 5_000);
        cts.Cancel();
        await worker.StopAsync(CancellationToken.None);

        var outboxA = await CountOutboxAsync(
            tenantA,
            "ShopFlow.Inventory.Domain.Events.StockReleasedEvent"
        );
        var outboxB = await CountOutboxAsync(
            tenantB,
            "ShopFlow.Inventory.Domain.Events.StockReleasedEvent"
        );
        outboxA.Should().Be(2);
        outboxB.Should().Be(1);

        // Cross-tenant guard: tenant A's outbox does not contain tenant B's data.
        var crossTalk = await CountOutboxMatchingPayloadAsync(tenantA, "OLD-B1");
        crossTalk.Should().Be(0);
    }

    [Fact]
    public async Task TenantWithBrokenDb_DoesNotBlockHealthyTenant()
    {
        var healthy = await _fx.ProvisionTenantAsync("worker-h");
        await _fx.SeedStockAsync(healthy, Sku, available: 100);
        await SeedExpiredReservationAsync(healthy, "OLD-HEALTHY", quantity: 3);
        await BumpReservedAsync(healthy, Sku, by: 3);

        // "Broken" tenant points at a database that does not exist.
        var brokenInfo = new TenantInfo(
            Id: Guid.NewGuid(),
            Slug: "worker-broken",
            DbName: "does_not_exist",
            DbConnectionString: new NpgsqlConnectionStringBuilder(_fx.AdminConnectionString)
            {
                Database = "does_not_exist",
            }.ConnectionString,
            Region: "ap-southeast-1",
            Tier: "free",
            Status: TenantStatus.Ready
        );
        var brokenTenant = new ProvisionedTenant(
            brokenInfo,
            new DbContextOptionsBuilder<InventoryDbContext>()
                .UseNpgsql(brokenInfo.DbConnectionString)
                .Options
        );

        var sp = BuildServiceProvider(intervalSeconds: 1, new[] { brokenTenant, healthy });
        var worker = sp.GetRequiredService<ReservationExpiryWorker>();

        using var cts = new CancellationTokenSource();
        await worker.StartAsync(cts.Token);

        await WaitForReleaseAsync(healthy, expectedExpiredCount: 1, timeoutMs: 5_000);
        cts.Cancel();
        await worker.StopAsync(CancellationToken.None);
    }

    private static async Task SeedExpiredReservationAsync(
        ProvisionedTenant tenant,
        string orderId,
        int quantity
    )
    {
        var past = DateTime.UtcNow.AddHours(-1);
        var expiredAt = past.AddMinutes(-15);
        await using var conn = new NpgsqlConnection(tenant.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO reservations_ledger
                (id, sku, order_id, quantity, status, expires_at, created_at)
            VALUES (@id, @sku, @order, @qty, 'Pending', @expires, @created)
            """;
        cmd.Parameters.AddWithValue("id", Guid.NewGuid());
        cmd.Parameters.AddWithValue("sku", Sku);
        cmd.Parameters.AddWithValue("order", orderId);
        cmd.Parameters.AddWithValue("qty", quantity);
        cmd.Parameters.AddWithValue("expires", expiredAt);
        cmd.Parameters.AddWithValue("created", past);
        await cmd.ExecuteNonQueryAsync();
    }

    private static async Task BumpReservedAsync(ProvisionedTenant tenant, string sku, int by)
    {
        await using var conn = new NpgsqlConnection(tenant.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText =
            "UPDATE stock_items SET reserved = reserved + @by, available = available - @by WHERE sku = @sku";
        cmd.Parameters.AddWithValue("by", by);
        cmd.Parameters.AddWithValue("sku", sku);
        await cmd.ExecuteNonQueryAsync();
    }

    private static async Task WaitForReleaseAsync(
        ProvisionedTenant tenant,
        int expectedExpiredCount,
        int timeoutMs
    )
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (DateTime.UtcNow < deadline)
        {
            var count = await CountExpiredAsync(tenant);
            if (count >= expectedExpiredCount)
            {
                return;
            }
            await Task.Delay(100);
        }
        var final = await CountExpiredAsync(tenant);
        throw new TimeoutException(
            $"Expected {expectedExpiredCount} expired rows in tenant {tenant.Info.Slug}, saw {final} after {timeoutMs}ms."
        );
    }

    private static async Task<int> CountExpiredAsync(ProvisionedTenant tenant)
    {
        await using var conn = new NpgsqlConnection(tenant.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText =
            "SELECT COUNT(*) FROM reservations_ledger WHERE status = 'Expired'";
        var scalar = (long)(await cmd.ExecuteScalarAsync())!;
        return (int)scalar;
    }

    private static async Task<int> CountOutboxAsync(ProvisionedTenant tenant, string typePrefix)
    {
        await using var conn = new NpgsqlConnection(tenant.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM inventory_outbox_messages WHERE event_type LIKE @p";
        cmd.Parameters.AddWithValue("p", typePrefix + "%");
        var scalar = (long)(await cmd.ExecuteScalarAsync())!;
        return (int)scalar;
    }

    private static async Task<int> CountOutboxMatchingPayloadAsync(
        ProvisionedTenant tenant,
        string payloadSubstring
    )
    {
        await using var conn = new NpgsqlConnection(tenant.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM inventory_outbox_messages WHERE payload::text LIKE @p";
        cmd.Parameters.AddWithValue("p", "%" + payloadSubstring + "%");
        var scalar = (long)(await cmd.ExecuteScalarAsync())!;
        return (int)scalar;
    }

    private static IServiceProvider BuildServiceProvider(
        int intervalSeconds,
        IReadOnlyList<ProvisionedTenant> tenants
    )
    {
        var services = new ServiceCollection();
        services.AddLogging(b => b.AddProvider(NullLoggerProvider.Instance));
        services.AddSingleton<TimeProvider>(TimeProvider.System);
        services.AddSingleton(
            Options.Create(
                new InventoryOptions
                {
                    ExpiryPollIntervalSeconds = intervalSeconds,
                    ExpiryBatchSize = 100,
                }
            )
        );

        var catalog = new FakeTenantCatalog(tenants.Select(t => t.Info).ToArray());
        services.AddSingleton<ITenantCatalog>(catalog);

        // Per-request scoped tenant context the worker binds before resolving repos.
        services.AddScoped<RequestContext>();
        services.AddScoped<IRequestContext>(sp => sp.GetRequiredService<RequestContext>());

        // DbContext bound to the current request's tenant connection string,
        // resolved via a lookup against the provisioned tenants by Id (the
        // worker sets RequestContext.TenantId via Bind).
        var tenantOptionsById = tenants.ToDictionary(t => t.Info.Id, t => t.Options);
        services.AddScoped(sp =>
        {
            var rc = sp.GetRequiredService<IRequestContext>();
            return new InventoryDbContext(tenantOptionsById[rc.TenantId]);
        });

        services.AddScoped<IReservationRepository, ReservationRepository>();
        services.AddSingleton<ReservationExpiryWorker>();

        return services.BuildServiceProvider();
    }

    private sealed class FakeTenantCatalog : ITenantCatalog
    {
        private readonly Dictionary<string, TenantInfo> _bySlug;
        private readonly Dictionary<Guid, TenantInfo> _byId;
        private readonly TenantInfo[] _all;

        public FakeTenantCatalog(params TenantInfo[] tenants)
        {
            _all = tenants;
            _bySlug = tenants.ToDictionary(t => t.Slug, StringComparer.OrdinalIgnoreCase);
            _byId = tenants.ToDictionary(t => t.Id);
        }

        public Task<TenantInfo?> LookupBySlugAsync(string slug, CancellationToken ct)
        {
            _bySlug.TryGetValue(slug, out var t);
            return Task.FromResult(t);
        }

        public Task<TenantInfo?> LookupByIdAsync(Guid tenantId, CancellationToken ct)
        {
            _byId.TryGetValue(tenantId, out var t);
            return Task.FromResult(t);
        }

        public Task<IReadOnlyList<TenantInfo>> GetReadyTenantsAsync(CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<TenantInfo>>(_all);
    }

    /// <summary>
    /// Stub scope factory for constructor-arg-validation tests (no DI needed).
    /// </summary>
    private sealed class EmptyScopeFactory : IServiceScopeFactory
    {
        public IServiceScope CreateScope() => throw new NotImplementedException();
    }
}
