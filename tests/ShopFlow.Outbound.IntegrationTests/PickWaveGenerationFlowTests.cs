using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using ShopFlow.Outbound.Application;
using ShopFlow.Outbound.Application.Ports;
using ShopFlow.Outbound.Domain;
using ShopFlow.Outbound.Infrastructure;
using ShopFlow.Outbound.Infrastructure.Repositories;
using ShopFlow.Outbound.Infrastructure.Workers;
using ShopFlow.SharedKernel.Application;
using ShopFlow.SharedKernel.Application.Ports;
using ShopFlow.SharedKernel.Domain;
using PickQueueImpl = ShopFlow.Outbound.Infrastructure.PickQueue.PickQueue;

namespace ShopFlow.Outbound.IntegrationTests;

/// <summary>
/// Sprint-3-redux U5 — end-to-end gate. Write 50 items into a tenant's
/// in-process <see cref="IPickQueue"/>, run one tick of the
/// <see cref="PickWaveGeneratorService"/>, and assert one
/// <c>pick_waves</c> row + 50 <c>pick_assignments</c> materialise in
/// the real Postgres tenant DB with the correct picker assignment +
/// each order's <c>pick_wave_id</c> populated.
/// </summary>
/// <remarks>
/// The unit-test sibling (<c>tests/ShopFlow.Outbound.UnitTests/PickWaveGenerator</c>)
/// exercises window-close timing logic against in-memory EF; this test
/// confirms the EF write actually lands in Postgres + the cascading
/// behaviour (orders.pick_wave_id update + pick_assignments FK) holds.
/// </remarks>
[Collection(OutboundTenantCollection.Name)]
[Trait("Category", "Integration")]
public sealed class PickWaveGenerationFlowTests : IAsyncLifetime
{
    private readonly OutboundTenantFixture _fx;
    private ProvisionedOutboundTenant _tenant = default!;

    public PickWaveGenerationFlowTests(OutboundTenantFixture fx)
    {
        _fx = fx;
    }

    public async Task InitializeAsync()
    {
        _tenant = await _fx.ProvisionTenantAsync("pick-flow");
        await SeedPickersAsync(_tenant, new[] { "picker-1", "picker-2", "picker-3" });
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task WriteFiftyItems_RunOneTick_MaterializesWaveWithFiftyAssignments()
    {
        // Wire the generator against the real tenant DB.
        var queue = new PickQueueImpl();
        var (generator, services) = BuildGenerator(queue, _tenant);

        // Seed 50 orders into the tenant DB so the generator's
        // FindByIdAsync resolves real rows + populates pick_wave_id.
        var orderIds = await SeedOrdersAsync(_tenant, count: 50, profile: "standard");

        // Write 50 PickRequests with EnqueuedAt = now-20 min so the
        // window-age trigger fires (size cap would also fire — either
        // is valid for the round-trip test).
        var enqueueAt = DateTime.UtcNow.AddMinutes(-20);
        foreach (var oid in orderIds)
        {
            queue
                .GetWriter(_tenant.Info.Id)
                .TryWrite(
                    new PickRequestV1(
                        OrderId: oid,
                        TenantId: _tenant.Info.Id,
                        ShippingProfile: "standard",
                        EnqueuedAt: enqueueAt,
                        LineCount: 1
                    )
                )
                .Should()
                .BeTrue();
        }

        await generator.TickAsync(CancellationToken.None);

        // Assert: one pick_waves row + 50 pick_assignments + all orders
        // carry the same pick_wave_id.
        await using var verify = new OutboundDbContext(_tenant.Options);
        var waves = await verify.PickWaves.Include(w => w.Assignments).ToListAsync();
        waves.Should().HaveCount(1, "one wave per (tenant, profile) flush");
        var wave = waves.Single();
        wave.Assignments.Should().HaveCount(50);
        wave.ShippingProfile.Should().Be("standard");
        wave.PickerId.Should().BeOneOf("picker-1", "picker-2", "picker-3");
        wave.ClosedAt.Should().NotBeNull();

        // Cross-check via raw SQL — defensive in case EF tracking masks
        // a write that didn't actually land in the rows.
        await using var conn = new NpgsqlConnection(_tenant.ConnectionString);
        await conn.OpenAsync();
        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "SELECT COUNT(*) FROM pick_assignments";
            ((long)(await cmd.ExecuteScalarAsync())!).Should().Be(50);
        }

        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "SELECT COUNT(*) FROM orders WHERE pick_wave_id = @wid";
            cmd.Parameters.AddWithValue("wid", wave.Id);
            ((long)(await cmd.ExecuteScalarAsync())!)
                .Should()
                .Be(50, "every seeded order should have its pick_wave_id updated to the new wave");
        }
    }

    [Fact]
    public async Task EmptyQueue_RunOneTick_NoWaveRowsMaterialize()
    {
        var queue = new PickQueueImpl();
        var (generator, _) = BuildGenerator(queue, _tenant);

        await generator.TickAsync(CancellationToken.None);

        await using var verify = new OutboundDbContext(_tenant.Options);
        (await verify.PickWaves.CountAsync()).Should().Be(0);
        (await verify.PickAssignments.CountAsync()).Should().Be(0);
    }

    // ── Test infrastructure ----------------------------------------------

    private (PickWaveGeneratorService Generator, ServiceProvider Services) BuildGenerator(
        IPickQueue queue,
        ProvisionedOutboundTenant tenant
    )
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<TimeProvider>(TimeProvider.System);
        services.AddSingleton<IPickQueue>(queue);

        services.AddSingleton<ITenantCatalog>(new SingleTenantCatalog(tenant.Info));

        services.AddScoped<RequestContext>();
        services.AddScoped<IRequestContext>(sp => sp.GetRequiredService<RequestContext>());

        // Per-tenant DbContext — reads connection string from the bound
        // RequestContext at construction. For this test only one tenant
        // is provisioned, so the lookup table is trivial.
        services.AddScoped(sp =>
        {
            var rc = sp.GetRequiredService<IRequestContext>();
            // For this single-tenant test the request context's connection
            // string IS the provisioned tenant's; bind directly.
            return new OutboundDbContext(tenant.Options);
        });

        services.AddScoped<IPickWaveRepository, PickWaveRepository>();
        services.AddScoped<IPickerRepository, PickerRepository>();
        services.AddScoped<IOrderRepository, OrderRepository>();
        services.AddScoped<IUnitOfWork, OutboundUnitOfWork>();

        var sp = services.BuildServiceProvider();

        var generator = new PickWaveGeneratorService(
            sp.GetRequiredService<IServiceScopeFactory>(),
            queue,
            TimeProvider.System,
            NullLogger<PickWaveGeneratorService>.Instance
        );

        return (generator, sp);
    }

    private sealed class SingleTenantCatalog : ITenantCatalog
    {
        private readonly TenantInfo _tenant;

        public SingleTenantCatalog(TenantInfo tenant)
        {
            _tenant = tenant;
        }

        public Task<TenantInfo?> LookupBySlugAsync(string slug, CancellationToken ct) =>
            Task.FromResult<TenantInfo?>(slug == _tenant.Slug ? _tenant : null);

        public Task<TenantInfo?> LookupByIdAsync(Guid tenantId, CancellationToken ct) =>
            Task.FromResult<TenantInfo?>(tenantId == _tenant.Id ? _tenant : null);

        public Task<IReadOnlyList<TenantInfo>> GetReadyTenantsAsync(CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<TenantInfo>>(new[] { _tenant });
    }

    private static async Task SeedPickersAsync(
        ProvisionedOutboundTenant tenant,
        IReadOnlyList<string> pickerIds
    )
    {
        await using var ctx = new OutboundDbContext(tenant.Options);
        foreach (var pid in pickerIds)
        {
            ctx.Pickers.Add(Picker.Create(pid, $"Picker {pid}"));
        }
        await ctx.SaveChangesAsync();
    }

    private static async Task<List<Guid>> SeedOrdersAsync(
        ProvisionedOutboundTenant tenant,
        int count,
        string profile
    )
    {
        await using var ctx = new OutboundDbContext(tenant.Options);
        var orderIds = new List<Guid>();
        for (var i = 0; i < count; i++)
        {
            var order = Order
                .Create($"ext-flow-{i:000}", profile, new[] { ("SKU-X", 1, (int?)1) })
                .Value!;
            ctx.Orders.Add(order);
            orderIds.Add(order.Id);
        }
        await ctx.SaveChangesAsync();
        return orderIds;
    }
}
