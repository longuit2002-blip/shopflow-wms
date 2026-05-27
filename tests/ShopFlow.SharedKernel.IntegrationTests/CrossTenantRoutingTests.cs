using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using ShopFlow.Inventory.Infrastructure;
using ShopFlow.SharedKernel.Application;
using ShopFlow.SharedKernel.Application.Ports;
using ShopFlow.SharedKernel.Domain;
using ShopFlow.SharedKernel.Infrastructure;
using ShopFlow.TestSupport;

namespace ShopFlow.SharedKernel.IntegrationTests;

/// <summary>
/// The single highest-stakes correctness property of the system: a
/// request bound to tenant A's slug must never read tenant B's data
/// (AGENTS.md §3.21). A failure here is a P0 incident.
/// </summary>
/// <remarks>
/// <para>The suite provisions two tenant DBs under a fresh
/// Testcontainers Postgres, seeds distinct <c>stock_items</c> rows in
/// each, then drives the <see cref="TenantRoutingMiddleware"/> with
/// synthetic <see cref="HttpContext"/>s carrying different headers and
/// asserts the resolved <see cref="RequestContext.DbConnectionString"/>
/// reads only the matching tenant's rows.</para>
///
/// <para>The middleware is tested in isolation (no TestServer / HTTP
/// stack); the contract under test is the binding of
/// <see cref="IRequestContext"/> from the resolved slug plus the
/// resulting DbConnectionString. Anything downstream (controllers,
/// repositories) consuming that binding is correctness-by-construction
/// — if the connection string is the wrong tenant's, every subsequent
/// query reads the wrong DB by definition.</para>
/// </remarks>
[Collection(PostgresCollection.Name)]
[Trait("Category", "Integration")]
[Trait("Category", "Proof")] // finish-line U1 — selectable via `task proofs`
public sealed class CrossTenantRoutingTests : IAsyncLifetime
{
    private const string TenantA = "tenant-a";
    private const string TenantB = "tenant-b";
    private const string TenantASku = "SKU-A-1";
    private const string TenantBSku = "SKU-B-1";

    private readonly PostgresFixture _postgres;
    private string _tenantAConn = string.Empty;
    private string _tenantBConn = string.Empty;
    private FakeTenantCatalog _catalog = default!;

    public CrossTenantRoutingTests(PostgresFixture postgres)
    {
        _postgres = postgres;
    }

    public async Task InitializeAsync()
    {
        _tenantAConn = await ProvisionTenantAsync(TenantA, TenantASku, available: 10);
        _tenantBConn = await ProvisionTenantAsync(TenantB, TenantBSku, available: 25);

        _catalog = new FakeTenantCatalog(
            new TenantInfo(
                Id: Guid.NewGuid(),
                Slug: TenantA,
                DbName: "shopflow_t_" + TenantA,
                DbConnectionString: _tenantAConn,
                Region: "ap-southeast-1",
                Tier: "free",
                Status: TenantStatus.Ready
            ),
            new TenantInfo(
                Id: Guid.NewGuid(),
                Slug: TenantB,
                DbName: "shopflow_t_" + TenantB,
                DbConnectionString: _tenantBConn,
                Region: "ap-southeast-1",
                Tier: "free",
                Status: TenantStatus.Ready
            )
        );
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [ProofFact]
    public async Task TenantAHeader_BindsRequestContext_ToTenantADb_AndReadsOnlyTenantARows()
    {
        var ctx = BuildHttpContext(headerSlug: TenantA);
        var requestContext = new RequestContext();

        var middleware = new TenantRoutingMiddleware(
            next: _ => Task.CompletedTask,
            logger: NullLogger<TenantRoutingMiddleware>.Instance
        );

        await middleware.InvokeAsync(ctx, _catalog, requestContext);

        ctx.Response.StatusCode.Should().Be(StatusCodes.Status200OK);
        requestContext.TenantSlug.Should().Be(TenantA);
        requestContext.DbConnectionString.Should().Be(_tenantAConn);

        var skus = await ReadStockItemSkusAsync(requestContext.DbConnectionString);
        skus.Should().ContainSingle().Which.Should().Be(TenantASku);
        skus.Should().NotContain(TenantBSku);
    }

    [ProofFact]
    public async Task TenantBHeader_BindsRequestContext_ToTenantBDb_AndReadsOnlyTenantBRows()
    {
        var ctx = BuildHttpContext(headerSlug: TenantB);
        var requestContext = new RequestContext();

        var middleware = new TenantRoutingMiddleware(
            next: _ => Task.CompletedTask,
            logger: NullLogger<TenantRoutingMiddleware>.Instance
        );

        await middleware.InvokeAsync(ctx, _catalog, requestContext);

        ctx.Response.StatusCode.Should().Be(StatusCodes.Status200OK);
        requestContext.TenantSlug.Should().Be(TenantB);
        requestContext.DbConnectionString.Should().Be(_tenantBConn);

        var skus = await ReadStockItemSkusAsync(requestContext.DbConnectionString);
        skus.Should().ContainSingle().Which.Should().Be(TenantBSku);
        skus.Should().NotContain(TenantASku);
    }

    [ProofFact]
    public async Task NoTenantContext_ReturnsBadRequest_AndDoesNotBind()
    {
        var ctx = BuildHttpContext(headerSlug: null);
        var requestContext = new RequestContext();

        var middleware = new TenantRoutingMiddleware(
            next: _ => Task.FromException(new InvalidOperationException("next must not run")),
            logger: NullLogger<TenantRoutingMiddleware>.Instance
        );

        await middleware.InvokeAsync(ctx, _catalog, requestContext);

        ctx.Response.StatusCode.Should().Be(StatusCodes.Status400BadRequest);
        var act = () => requestContext.TenantSlug;
        act.Should().Throw<InvalidOperationException>();
    }

    [ProofFact]
    public async Task UnknownSlug_ReturnsNotFound()
    {
        var ctx = BuildHttpContext(headerSlug: "ghost-tenant");
        var requestContext = new RequestContext();

        var middleware = new TenantRoutingMiddleware(
            next: _ => Task.FromException(new InvalidOperationException("next must not run")),
            logger: NullLogger<TenantRoutingMiddleware>.Instance
        );

        await middleware.InvokeAsync(ctx, _catalog, requestContext);

        ctx.Response.StatusCode.Should().Be(StatusCodes.Status404NotFound);
    }

    [ProofFact]
    public async Task ConflictingHeaderAndSubdomain_Returns403_AndDoesNotBind()
    {
        var ctx = BuildHttpContext(headerSlug: TenantA);
        // Set a conflicting subdomain (host: tenant-b.shopflow.local).
        ctx.Request.Host = new HostString(TenantB + ".shopflow.local");
        var requestContext = new RequestContext();

        var middleware = new TenantRoutingMiddleware(
            next: _ => Task.FromException(new InvalidOperationException("next must not run")),
            logger: NullLogger<TenantRoutingMiddleware>.Instance
        );

        await middleware.InvokeAsync(ctx, _catalog, requestContext);

        ctx.Response.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
        var act = () => requestContext.TenantSlug;
        act.Should().Throw<InvalidOperationException>();
    }

    private async Task<string> ProvisionTenantAsync(string slug, string sku, int available)
    {
        var dbName = "shopflow_t_" + slug + "_" + Guid.NewGuid().ToString("N")[..8];
        var connStr = await _postgres.CreateDatabaseAsync(dbName);

        var options = new DbContextOptionsBuilder<InventoryDbContext>()
            .UseNpgsql(connStr, npg => npg.MigrationsAssembly("ShopFlow.Inventory.Infrastructure"))
            .Options;

        await using (var ctx = new InventoryDbContext(options))
        {
            await ctx.Database.MigrateAsync();
        }

        await using var conn = new NpgsqlConnection(connStr);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText =
            "INSERT INTO stock_items (sku, available, reserved, created_at) "
            + "VALUES (@sku, @avail, 0, @now)";
        cmd.Parameters.AddWithValue("sku", sku);
        cmd.Parameters.AddWithValue("avail", available);
        cmd.Parameters.AddWithValue("now", DateTime.UtcNow);
        await cmd.ExecuteNonQueryAsync();

        return connStr;
    }

    private static async Task<List<string>> ReadStockItemSkusAsync(string connStr)
    {
        var skus = new List<string>();
        await using var conn = new NpgsqlConnection(connStr);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT sku FROM stock_items ORDER BY sku";
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            skus.Add(reader.GetString(0));
        }
        return skus;
    }

    private static DefaultHttpContext BuildHttpContext(string? headerSlug)
    {
        var ctx = new DefaultHttpContext();
        ctx.Request.Host = new HostString("api.shopflow.local");
        if (headerSlug is not null)
        {
            ctx.Request.Headers[TenantRoutingMiddleware.TenantHeader] = headerSlug;
        }
        ctx.Response.Body = new MemoryStream();
        return ctx;
    }

    private sealed class FakeTenantCatalog : ITenantCatalog
    {
        private readonly Dictionary<string, TenantInfo> _bySlug;
        private readonly Dictionary<Guid, TenantInfo> _byId;

        public FakeTenantCatalog(params TenantInfo[] tenants)
        {
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
            Task.FromResult<IReadOnlyList<TenantInfo>>(_bySlug.Values.ToList());
    }
}
