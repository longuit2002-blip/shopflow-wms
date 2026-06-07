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
/// Sprint-3-redux U9 / AE4 — pick-wave batching flow integration test.
/// Drives 50 orders (30 standard + 20 express) through to <c>Reserved</c>
/// state, runs one tick of <see cref="PickWaveGeneratorService"/>, and
/// asserts two <c>pick_waves</c> rows materialize — one per shipping
/// profile — with the expected 30/20 PickAssignment split and round-robin
/// picker assignment across the seeded pool.
/// </summary>
/// <remarks>
/// <para><strong>Scope.</strong> The 50-order fan-out + mixed-profile
/// batching is the AE4 invariant under test. The saga upstream (which
/// would normally enqueue each PickRequest after StockReservedV1 lands)
/// is covered end-to-end by <see cref="SagaHappyPathTests"/> on ONE order
/// and by <see cref="CrossModuleReservationFlowTests"/> on the
/// cross-module side. Driving 50 saga instances through the in-memory
/// MassTransit harness with the EF saga repo's pessimistic lock serializes
/// the commits and balloons wall-time into the minute range — orthogonal
/// to what this test measures (the wave generator's per-profile fan-out +
/// round-robin picker assignment).</para>
///
/// <para><strong>Pattern.</strong> Mirrors <see cref="PickWaveGenerationFlowTests"/>:
/// seed 50 Order rows + 3 Picker rows in the tenant DB, push 50
/// PickRequests into the queue with stable
/// <c>EnqueuedAt = now - 20 min</c> so the generator's window-age trigger
/// fires immediately, then invoke <see cref="PickWaveGeneratorService.TickAsync"/>
/// directly (bypassing the 30s timer). Where the sibling test uses a
/// single shipping profile to focus on the generator's pure
/// queue-→-materialize path, this test exercises the per-profile fan-out
/// + the round-robin picker behaviour the wave generator carries.</para>
///
/// <para>Per-PR speed: expected wall-time ~3-5s on dev hardware.</para>
/// </remarks>
[Collection(OutboundTenantCollection.Name)]
[Trait("Category", "Integration")]
public sealed class PickWaveBatchingFlowTests : IAsyncLifetime
{
    private const int StandardCount = 30;
    private const int ExpressCount = 20;

    private readonly OutboundTenantFixture _fx;
    private ProvisionedOutboundTenant _tenant = default!;

    public PickWaveBatchingFlowTests(OutboundTenantFixture fx)
    {
        _fx = fx;
    }

    public async Task InitializeAsync()
    {
        _tenant = await _fx.ProvisionTenantAsync("pick-batch");
        await SeedPickersAsync(new[] { "picker-1", "picker-2", "picker-3" });
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task FiftyOrders_MixedProfiles_OneTick_MaterializesTwoWavesWithRoundRobinPicker()
    {
        var queue = new PickQueueImpl();
        var tenantId = _tenant.Info.Id;
        var stableEnqueuedAt = DateTime.UtcNow.AddMinutes(-20);

        var standardOrders = await SeedOrdersAsync(StandardCount, "standard");
        var expressOrders = await SeedOrdersAsync(ExpressCount, "express");

        // 50 PickRequests with stable old EnqueuedAt → window-age trigger
        // fires immediately on the next generator tick.
        foreach (var oid in standardOrders)
        {
            queue
                .GetWriter(tenantId)
                .TryWrite(
                    new PickRequestV1(
                        OrderId: oid,
                        TenantId: tenantId,
                        ShippingProfile: "standard",
                        EnqueuedAt: stableEnqueuedAt,
                        LineCount: 1
                    )
                )
                .Should()
                .BeTrue();
        }
        foreach (var oid in expressOrders)
        {
            queue
                .GetWriter(tenantId)
                .TryWrite(
                    new PickRequestV1(
                        OrderId: oid,
                        TenantId: tenantId,
                        ShippingProfile: "express",
                        EnqueuedAt: stableEnqueuedAt,
                        LineCount: 1
                    )
                )
                .Should()
                .BeTrue();
        }

        var (generator, _) = BuildGenerator(queue);
        await generator.TickAsync(CancellationToken.None);

        // ── Assertions ──────────────────────────────────────────────────
        await using var verify = new OutboundDbContext(_tenant.Options);
        var waves = await verify
            .PickWaves.Include(w => w.Assignments)
            .OrderBy(w => w.ShippingProfile)
            .ToListAsync();
        waves.Should().HaveCount(2, "one wave per shipping_profile flushed");

        var express = waves.Single(w => w.ShippingProfile == "express");
        var standard = waves.Single(w => w.ShippingProfile == "standard");

        express.Assignments.Should().HaveCount(ExpressCount);
        standard.Assignments.Should().HaveCount(StandardCount);

        // Round-robin picker assignment — each wave got a picker from the
        // seeded pool; with 2 waves and 3 pickers the two ids must differ.
        express.PickerId.Should().BeOneOf("picker-1", "picker-2", "picker-3");
        standard.PickerId.Should().BeOneOf("picker-1", "picker-2", "picker-3");
        express.PickerId.Should().NotBe(standard.PickerId);

        // Both waves were closed on this tick.
        express.ClosedAt.Should().NotBeNull();
        standard.ClosedAt.Should().NotBeNull();

        // Orders carry pick_wave_id matching the materialized wave.
        await using var conn = new NpgsqlConnection(_tenant.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT shipping_profile, COUNT(*)
              FROM orders
             WHERE pick_wave_id IS NOT NULL
            GROUP BY shipping_profile
            """;
        await using var reader = await cmd.ExecuteReaderAsync();
        var byProfile = new Dictionary<string, long>(StringComparer.Ordinal);
        while (await reader.ReadAsync())
        {
            byProfile[reader.GetString(0)] = reader.GetInt64(1);
        }
        byProfile.GetValueOrDefault("standard").Should().Be(StandardCount);
        byProfile.GetValueOrDefault("express").Should().Be(ExpressCount);
    }

    // ── Harness wiring ────────────────────────────────────────────────────

    private (PickWaveGeneratorService Generator, ServiceProvider Services) BuildGenerator(
        IPickQueue queue
    )
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<TimeProvider>(TimeProvider.System);
        services.AddSingleton<IPickQueue>(queue);

        services.AddSingleton<ITenantCatalog>(new SingleTenantCatalog(_tenant.Info));
        services.AddScoped<RequestContext>();
        services.AddScoped<IRequestContext>(sp => sp.GetRequiredService<RequestContext>());

        services.AddScoped(sp => new OutboundDbContext(_tenant.Options));
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

    private async Task SeedPickersAsync(IReadOnlyList<string> pickerIds)
    {
        await using var ctx = new OutboundDbContext(_tenant.Options);
        foreach (var pid in pickerIds)
        {
            ctx.Pickers.Add(Picker.Create(pid, $"Picker {pid}"));
        }
        await ctx.SaveChangesAsync();
    }

    private async Task<List<Guid>> SeedOrdersAsync(int count, string profile)
    {
        await using var ctx = new OutboundDbContext(_tenant.Options);
        var orderIds = new List<Guid>();
        for (var i = 0; i < count; i++)
        {
            var order = Order
                .Create(
                    $"ext-batch-{profile}-{i:000}-{Guid.NewGuid():N}",
                    profile,
                    new[] { ("SKU-X", 1, (int?)100) }
                )
                .Value!;
            ctx.Orders.Add(order);
            orderIds.Add(order.Id);
        }
        await ctx.SaveChangesAsync();
        return orderIds;
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
}
