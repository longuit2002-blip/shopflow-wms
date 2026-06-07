using MassTransit;
using MassTransit.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using ShopFlow.Contracts.Outbound;
using ShopFlow.Outbound.Application.Sagas;
using ShopFlow.Outbound.Infrastructure;
using ShopFlow.Outbound.Infrastructure.Sagas;
using ShopFlow.SharedKernel.Application;
using ShopFlow.SharedKernel.Application.Ports;
using ShopFlow.SharedKernel.Domain;

namespace ShopFlow.Outbound.IntegrationTests;

/// <summary>
/// Sprint-3-redux U4 K12 — LOAD-BEARING test for the per-tenant saga
/// DbContext binding. Two physical tenant databases are provisioned; one
/// <c>OrderPlacedV1</c> envelope is published for each tenant with the
/// envelope's <c>tenant_id</c> header pointing at the correct tenant.
/// </summary>
/// <remarks>
/// <para>The assertion: tenant-A's <c>saga_state</c> contains the
/// tenant-A order's row + tenant-B's <c>saga_state</c> contains the
/// tenant-B order's row + neither sees the other's row. Cross-tenant
/// contamination here would mean K12 failed — saga writes were going to
/// the wrong tenant DB.</para>
///
/// <para>K12 primary path = <see cref="TenantBindingSagaFilter{T}"/>
/// registered on the saga's receive endpoint. The filter reads the
/// envelope header, looks up the tenant via the in-memory
/// <see cref="ITenantCatalog"/> fake (seeded with the two test tenants),
/// and binds the scoped <see cref="RequestContext"/> BEFORE the saga
/// repository's DbContext resolution runs.</para>
///
/// <para>OutboundDbContext is registered as scoped per the production
/// shape — its constructor reads <see cref="IRequestContext.DbConnectionString"/>
/// at injection time, so per-message scope = per-tenant DbContext.</para>
/// </remarks>
[Collection(OutboundTenantCollection.Name)]
[Trait("Category", "Integration")]
public sealed class SagaPerTenantBindingTests : IAsyncLifetime
{
    private readonly OutboundTenantFixture _fx;
    private ProvisionedOutboundTenant _tenantA = default!;
    private ProvisionedOutboundTenant _tenantB = default!;

    public SagaPerTenantBindingTests(OutboundTenantFixture fx)
    {
        _fx = fx;
    }

    public async Task InitializeAsync()
    {
        _tenantA = await _fx.ProvisionTenantAsync("alpha");
        _tenantB = await _fx.ProvisionTenantAsync("beta");
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private static readonly IReadOnlyList<OrderPlacedLineV1> TwoLines = new[]
    {
        new OrderPlacedLineV1(OrderLineId: "L1", Sku: "SKU-A", Qty: 2, ExpectedWeight: 100),
        new OrderPlacedLineV1(OrderLineId: "L2", Sku: "SKU-B", Qty: 5, ExpectedWeight: 50),
    };

    /// <summary>
    /// In-memory <see cref="ITenantCatalog"/> seeded with the two provisioned
    /// test tenants. The filter pulls this from DI to resolve tenant ids
    /// → <see cref="TenantInfo"/> → connection strings.
    /// </summary>
    private sealed class FakeTenantCatalog : ITenantCatalog
    {
        private readonly Dictionary<Guid, TenantInfo> _byId;

        public FakeTenantCatalog(params TenantInfo[] tenants) =>
            _byId = tenants.ToDictionary(t => t.Id);

        public Task<TenantInfo?> LookupByIdAsync(Guid tenantId, CancellationToken ct) =>
            Task.FromResult(_byId.TryGetValue(tenantId, out var t) ? t : null);

        public Task<TenantInfo?> LookupBySlugAsync(string slug, CancellationToken ct) =>
            Task.FromResult<TenantInfo?>(_byId.Values.FirstOrDefault(v => v.Slug == slug));

        public Task<IReadOnlyList<TenantInfo>> GetReadyTenantsAsync(CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<TenantInfo>>(_byId.Values.ToList());
    }

    private async Task<ServiceProvider> BuildHostAsync()
    {
        var services = new ServiceCollection();
        services.AddLogging(b => b.AddProvider(NullLoggerProvider.Instance));

        services.AddSingleton<ITenantCatalog>(new FakeTenantCatalog(_tenantA.Info, _tenantB.Info));

        // Scoped RequestContext per consume scope — TenantBindingSagaFilter
        // binds it from the envelope header at message-receive time.
        services.AddScoped<RequestContext>();
        services.AddScoped<IRequestContext>(sp => sp.GetRequiredService<RequestContext>());

        // Per-tenant DbContext: resolves IRequestContext.DbConnectionString
        // per injection. After TenantBindingSagaFilter binds the request
        // context, this construction picks the correct tenant DB.
        services.AddScoped<OutboundDbContext>(sp =>
        {
            var ctx = sp.GetRequiredService<IRequestContext>();
            var options = new DbContextOptionsBuilder<OutboundDbContext>()
                .UseNpgsql(
                    ctx.DbConnectionString,
                    npg => npg.MigrationsAssembly("ShopFlow.Outbound.Infrastructure")
                )
                .Options;
            return new OutboundDbContext(options);
        });

        services.AddScoped(typeof(TenantBindingSagaFilter<>));

        services.AddMassTransitTestHarness(cfg =>
        {
            cfg.AddSagaStateMachine<FulfillmentSaga, FulfillmentSagaState>()
                .EntityFrameworkRepository(r =>
                {
                    r.ExistingDbContext<OutboundDbContext>();
                    r.UsePostgres();
                });

            cfg.UsingInMemory(
                (context, busCfg) =>
                {
                    // K12 primary path — the open-generic filter is wired
                    // on the bus's receive endpoint via UseConsumeFilter
                    // so every typed message that flows through this bus
                    // passes through tenant binding before reaching the
                    // saga repository's DbContext resolution.
                    busCfg.UseConsumeFilter(typeof(TenantBindingSagaFilter<>), context);
                    busCfg.ConfigureEndpoints(context);
                }
            );
        });

        var sp = services.BuildServiceProvider(true);
        await sp.GetRequiredService<ITestHarness>().Start();
        return sp;
    }

    [Fact]
    public async Task TwoTenants_SagaStateRows_LandInTheirOwnDatabases()
    {
        await using var sp = await BuildHostAsync();
        var harness = sp.GetRequiredService<ITestHarness>();

        var orderA = Guid.NewGuid();
        var orderB = Guid.NewGuid();

        await harness.Bus.Publish(
            new OrderPlacedV1(
                orderA,
                _tenantA.Info.Id,
                "ext-a",
                "standard",
                TwoLines,
                DateTime.UtcNow
            ),
            ctx =>
                ctx.Headers.Set(
                    TenantBindingSagaFilter<OrderPlacedV1>.TenantIdHeader,
                    _tenantA.Info.Id.ToString()
                )
        );
        await harness.Bus.Publish(
            new OrderPlacedV1(
                orderB,
                _tenantB.Info.Id,
                "ext-b",
                "express",
                TwoLines,
                DateTime.UtcNow
            ),
            ctx =>
                ctx.Headers.Set(
                    TenantBindingSagaFilter<OrderPlacedV1>.TenantIdHeader,
                    _tenantB.Info.Id.ToString()
                )
        );

        // Wait for BOTH consume operations to settle (Consumed.Any returns
        // true on the FIRST one, so block until both rows materialize).
        (await harness.Consumed.SelectAsync<OrderPlacedV1>().Take(2).Count())
            .Should()
            .Be(2);
        await WaitForRowAsync(_tenantA.ConnectionString, orderA);
        await WaitForRowAsync(_tenantB.ConnectionString, orderB);

        // Assertion 1: tenant-A's DB has only orderA's saga.
        var tenantARows = await ReadAllSagaRowsAsync(_tenantA.ConnectionString);
        tenantARows.Should().ContainSingle(r => r.CorrelationId == orderA);
        tenantARows
            .Should()
            .NotContain(
                r => r.CorrelationId == orderB,
                "tenant-A's saga_state must NOT contain tenant-B's order — K12 contamination"
            );

        // Assertion 2: tenant-B's DB has only orderB's saga.
        var tenantBRows = await ReadAllSagaRowsAsync(_tenantB.ConnectionString);
        tenantBRows.Should().ContainSingle(r => r.CorrelationId == orderB);
        tenantBRows
            .Should()
            .NotContain(
                r => r.CorrelationId == orderA,
                "tenant-B's saga_state must NOT contain tenant-A's order — K12 contamination"
            );

        // Assertion 3: TenantId column on each row matches its tenant.
        tenantARows.Single(r => r.CorrelationId == orderA).TenantId.Should().Be(_tenantA.Info.Id);
        tenantBRows.Single(r => r.CorrelationId == orderB).TenantId.Should().Be(_tenantB.Info.Id);
    }

    private static async Task WaitForRowAsync(string connStr, Guid correlationId)
    {
        var deadline = DateTime.UtcNow.AddSeconds(15);
        while (DateTime.UtcNow < deadline)
        {
            await using var conn = new NpgsqlConnection(connStr);
            await conn.OpenAsync();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = """SELECT COUNT(*) FROM saga_state WHERE "CorrelationId" = @oid""";
            cmd.Parameters.AddWithValue("oid", correlationId);
            var count = (long)(await cmd.ExecuteScalarAsync())!;
            if (count >= 1)
            {
                return;
            }
            await Task.Delay(100);
        }
        throw new TimeoutException(
            $"saga_state row for {correlationId} did not appear in {connStr} within 15s."
        );
    }

    private sealed record SagaRow(Guid CorrelationId, string CurrentState, Guid TenantId);

    private static async Task<List<SagaRow>> ReadAllSagaRowsAsync(string connStr)
    {
        var rows = new List<SagaRow>();
        await using var conn = new NpgsqlConnection(connStr);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """SELECT "CorrelationId", "CurrentState", tenant_id FROM saga_state""";
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            rows.Add(new SagaRow(reader.GetGuid(0), reader.GetString(1), reader.GetGuid(2)));
        }
        return rows;
    }
}
