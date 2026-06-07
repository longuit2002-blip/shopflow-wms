using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using ShopFlow.Outbound.Application;
using ShopFlow.Outbound.Application.Ports;
using ShopFlow.Outbound.Domain;
using ShopFlow.Outbound.Infrastructure;
using ShopFlow.Outbound.Infrastructure.Workers;
using ShopFlow.SharedKernel.Application;
using ShopFlow.SharedKernel.Application.Ports;
using ShopFlow.SharedKernel.Domain;
using PickQueueImpl = ShopFlow.Outbound.Infrastructure.PickQueue.PickQueue;

namespace ShopFlow.Outbound.UnitTests.PickWaveGeneratorTests;

/// <summary>
/// Sprint-3-redux U5 â€” <see cref="PickWaveGeneratorService"/> window-close
/// logic in isolation. Exercises the internal <c>TickAsync</c> entry
/// point against an in-memory <see cref="IPickQueue"/> + in-memory EF
/// Core context for the per-tenant DB stand-in. The
/// <see cref="DeterministicTimeProvider"/> below substitutes for the
/// .NET 8+ <c>FakeTimeProvider</c> package (which is not yet referenced
/// in this repo's CPM); it implements just enough of
/// <see cref="TimeProvider"/> for the generator's <c>GetUtcNow</c>
/// readback (no <see cref="System.Threading.PeriodicTimer"/> involvement
/// here because the tests invoke <c>TickAsync</c> directly).
/// </summary>
public sealed class PickWaveGeneratorTests
{
    private static readonly DateTime FixedNow = new DateTime(
        2026,
        5,
        13,
        10,
        0,
        0,
        DateTimeKind.Utc
    );

    // Per-test instance fields so each test gets a fresh InMemory db name.
    // (xUnit constructs a new instance per [Fact], so each test sees a
    // unique tenant id + unique InMemory backing store.)
    private readonly Guid _tenantAId = Guid.NewGuid();
    private readonly Guid _tenantBId = Guid.NewGuid();

    private TenantInfo TenantA() =>
        new(
            Id: _tenantAId,
            Slug: "tenant-a",
            DbName: $"db_a_{_tenantAId:N}",
            DbConnectionString: $"Filename=db_a_{_tenantAId:N}.dat",
            Region: "ap-southeast-1",
            Tier: "free",
            Status: TenantStatus.Ready
        );

    private TenantInfo TenantB() =>
        new(
            Id: _tenantBId,
            Slug: "tenant-b",
            DbName: $"db_b_{_tenantBId:N}",
            DbConnectionString: $"Filename=db_b_{_tenantBId:N}.dat",
            Region: "ap-southeast-1",
            Tier: "free",
            Status: TenantStatus.Ready
        );

    // â”€â”€ Window-close by TIME ----------------------------------------------

    [Fact]
    public async Task Tick_WindowAgedPast15Min_EmitsWave()
    {
        var clock = new DeterministicTimeProvider(FixedNow);
        var queue = new PickQueueImpl();
        var sut = BuildSut(
            clock,
            queue,
            new[] { TenantA() },
            seedPickersForTenant: _tenantAId,
            pickerIds: new[] { "picker-1", "picker-2", "picker-3" }
        );

        // Write 5 items into the tenant's queue, EnqueuedAt = now - 16 min.
        var enqueueAt = FixedNow.AddMinutes(-16);
        for (var i = 0; i < 5; i++)
        {
            queue
                .GetWriter(_tenantAId)
                .TryWrite(
                    new PickRequestV1(
                        OrderId: Guid.NewGuid(),
                        TenantId: _tenantAId,
                        ShippingProfile: "standard",
                        EnqueuedAt: enqueueAt,
                        LineCount: 1
                    )
                )
                .Should()
                .BeTrue();
        }

        // Pre-seed empty orders rows referencing the queued OrderIds so
        // the generator's order.AttachToPickWave call finds rows.
        await SeedOrdersForQueuedItemsAsync(sut, queue, _tenantAId);

        // Run one tick.
        await sut.Generator.TickAsync(CancellationToken.None);

        // Assert: one wave, five assignments.
        await using var verify = new OutboundDbContext(sut.OptionsByTenantId[_tenantAId]);
        var waves = await verify.PickWaves.Include(w => w.Assignments).ToListAsync();
        waves.Should().HaveCount(1);
        waves.Single().Assignments.Should().HaveCount(5);
        waves.Single().ShippingProfile.Should().Be("standard");
        waves.Single().ClosedAt.Should().Be(FixedNow);
    }

    // â”€â”€ Window-close by SIZE ---------------------------------------------

    [Fact]
    public async Task Tick_BufferReaches50_EmitsWaveImmediately()
    {
        var clock = new DeterministicTimeProvider(FixedNow);
        var queue = new PickQueueImpl();
        var sut = BuildSut(
            clock,
            queue,
            new[] { TenantA() },
            seedPickersForTenant: _tenantAId,
            pickerIds: new[] { "picker-1" }
        );

        // 50 items, all enqueued NOW â€” age trigger NOT hit, but size cap is.
        for (var i = 0; i < 50; i++)
        {
            queue
                .GetWriter(_tenantAId)
                .TryWrite(
                    new PickRequestV1(
                        OrderId: Guid.NewGuid(),
                        TenantId: _tenantAId,
                        ShippingProfile: "standard",
                        EnqueuedAt: FixedNow,
                        LineCount: 1
                    )
                )
                .Should()
                .BeTrue();
        }

        await SeedOrdersForQueuedItemsAsync(sut, queue, _tenantAId);
        await sut.Generator.TickAsync(CancellationToken.None);

        await using var verify = new OutboundDbContext(sut.OptionsByTenantId[_tenantAId]);
        var waves = await verify.PickWaves.Include(w => w.Assignments).ToListAsync();
        waves.Should().HaveCount(1);
        waves.Single().Assignments.Should().HaveCount(50);
    }

    // â”€â”€ Group-by shipping_profile (AE4) ----------------------------------

    [Fact]
    public async Task Tick_TwoShippingProfiles_BothAged_EmitsTwoWaves()
    {
        var clock = new DeterministicTimeProvider(FixedNow);
        var queue = new PickQueueImpl();
        var sut = BuildSut(
            clock,
            queue,
            new[] { TenantA() },
            seedPickersForTenant: _tenantAId,
            pickerIds: new[] { "picker-1", "picker-2" }
        );

        var aged = FixedNow.AddMinutes(-20);
        for (var i = 0; i < 30; i++)
        {
            queue
                .GetWriter(_tenantAId)
                .TryWrite(new PickRequestV1(Guid.NewGuid(), _tenantAId, "standard", aged, 1));
        }
        for (var i = 0; i < 20; i++)
        {
            queue
                .GetWriter(_tenantAId)
                .TryWrite(new PickRequestV1(Guid.NewGuid(), _tenantAId, "express", aged, 1));
        }

        await SeedOrdersForQueuedItemsAsync(sut, queue, _tenantAId);
        await sut.Generator.TickAsync(CancellationToken.None);

        await using var verify = new OutboundDbContext(sut.OptionsByTenantId[_tenantAId]);
        var waves = await verify.PickWaves.Include(w => w.Assignments).ToListAsync();
        waves.Should().HaveCount(2);
        waves.Should().Contain(w => w.ShippingProfile == "standard" && w.Assignments.Count == 30);
        waves.Should().Contain(w => w.ShippingProfile == "express" && w.Assignments.Count == 20);
    }

    // â”€â”€ Round-robin picker assignment ------------------------------------

    [Fact]
    public async Task Tick_ConsecutiveWaves_AssignPickersRoundRobin()
    {
        var clock = new DeterministicTimeProvider(FixedNow);
        var queue = new PickQueueImpl();
        var sut = BuildSut(
            clock,
            queue,
            new[] { TenantA() },
            seedPickersForTenant: _tenantAId,
            pickerIds: new[] { "picker-1", "picker-2", "picker-3" }
        );

        // Three separate ticks, each producing one wave.
        var pickerSequence = new List<string>();
        for (var wave = 0; wave < 3; wave++)
        {
            for (var i = 0; i < 50; i++)
            {
                queue
                    .GetWriter(_tenantAId)
                    .TryWrite(
                        new PickRequestV1(
                            Guid.NewGuid(),
                            _tenantAId,
                            $"profile-{wave}",
                            FixedNow,
                            1
                        )
                    );
            }
            await SeedOrdersForQueuedItemsAsync(sut, queue, _tenantAId);
            await sut.Generator.TickAsync(CancellationToken.None);

            await using var verify = new OutboundDbContext(sut.OptionsByTenantId[_tenantAId]);
            var thisWave = await verify
                .PickWaves.OrderBy(w => w.CreatedAt)
                .Where(w => w.ShippingProfile == $"profile-{wave}")
                .SingleAsync();
            pickerSequence.Add(thisWave.PickerId);
        }

        pickerSequence
            .Should()
            .BeEquivalentTo(
                new[] { "picker-1", "picker-2", "picker-3" },
                opts => opts.WithStrictOrdering(),
                "the round-robin cursor must step through pickers ordered by picker_id"
            );
    }

    // â”€â”€ Per-tenant isolation ---------------------------------------------

    [Fact]
    public async Task Tick_TenantWithNoPickers_DoesNotBlockHealthyTenant()
    {
        var clock = new DeterministicTimeProvider(FixedNow);
        var queue = new PickQueueImpl();
        // Tenant A has pickers + one queued item, tenant B has no pickers.
        // The generator should emit A's wave + skip B's emit without
        // throwing.
        var sut = BuildSut(
            clock,
            queue,
            new[] { TenantA(), TenantB() },
            seedPickersForTenant: _tenantAId,
            pickerIds: new[] { "picker-a" }
        );

        var aged = FixedNow.AddMinutes(-20);
        queue
            .GetWriter(_tenantAId)
            .TryWrite(new PickRequestV1(Guid.NewGuid(), _tenantAId, "standard", aged, 1));
        queue
            .GetWriter(_tenantBId)
            .TryWrite(new PickRequestV1(Guid.NewGuid(), _tenantBId, "standard", aged, 1));

        await SeedOrdersForQueuedItemsAsync(sut, queue, _tenantAId);
        await SeedOrdersForQueuedItemsAsync(sut, queue, _tenantBId);
        await sut.Generator.TickAsync(CancellationToken.None);

        await using var verifyA = new OutboundDbContext(sut.OptionsByTenantId[_tenantAId]);
        (await verifyA.PickWaves.CountAsync()).Should().Be(1);

        await using var verifyB = new OutboundDbContext(sut.OptionsByTenantId[_tenantBId]);
        (await verifyB.PickWaves.CountAsync()).Should().Be(0);
    }

    // â”€â”€ No-op tick -------------------------------------------------------

    [Fact]
    public async Task Tick_EmptyChannels_NoWavesEmitted()
    {
        var clock = new DeterministicTimeProvider(FixedNow);
        var queue = new PickQueueImpl();
        var sut = BuildSut(
            clock,
            queue,
            new[] { TenantA() },
            seedPickersForTenant: _tenantAId,
            pickerIds: new[] { "picker-1" }
        );

        await sut.Generator.TickAsync(CancellationToken.None);

        await using var verify = new OutboundDbContext(sut.OptionsByTenantId[_tenantAId]);
        (await verify.PickWaves.CountAsync()).Should().Be(0);
    }

    // â”€â”€ Buffer NOT-yet-aged stays put ------------------------------------

    [Fact]
    public async Task Tick_BufferYoungerThan15Min_DoesNotFlush()
    {
        var clock = new DeterministicTimeProvider(FixedNow);
        var queue = new PickQueueImpl();
        var sut = BuildSut(
            clock,
            queue,
            new[] { TenantA() },
            seedPickersForTenant: _tenantAId,
            pickerIds: new[] { "picker-1" }
        );

        // 5 items aged 5 minutes â€” neither size cap nor age trigger fires.
        var fresh = FixedNow.AddMinutes(-5);
        for (var i = 0; i < 5; i++)
        {
            queue
                .GetWriter(_tenantAId)
                .TryWrite(new PickRequestV1(Guid.NewGuid(), _tenantAId, "standard", fresh, 1));
        }

        await SeedOrdersForQueuedItemsAsync(sut, queue, _tenantAId);
        await sut.Generator.TickAsync(CancellationToken.None);

        await using var verify = new OutboundDbContext(sut.OptionsByTenantId[_tenantAId]);
        (await verify.PickWaves.CountAsync()).Should().Be(0);
    }

    // â”€â”€ Buffer carries items across ticks until window matures ----------

    [Fact]
    public async Task Tick_BufferAcrossTicks_FlushWhenAgeTriggerHits()
    {
        var clock = new DeterministicTimeProvider(FixedNow);
        var queue = new PickQueueImpl();
        var sut = BuildSut(
            clock,
            queue,
            new[] { TenantA() },
            seedPickersForTenant: _tenantAId,
            pickerIds: new[] { "picker-1" }
        );

        var enqueueAt = FixedNow;
        for (var i = 0; i < 5; i++)
        {
            queue
                .GetWriter(_tenantAId)
                .TryWrite(new PickRequestV1(Guid.NewGuid(), _tenantAId, "standard", enqueueAt, 1));
        }
        await SeedOrdersForQueuedItemsAsync(sut, queue, _tenantAId);

        // Tick 1 â€” items just enqueued; nothing flushed.
        await sut.Generator.TickAsync(CancellationToken.None);
        await using (var v1 = new OutboundDbContext(sut.OptionsByTenantId[_tenantAId]))
        {
            (await v1.PickWaves.CountAsync()).Should().Be(0);
        }

        // Advance the clock past 15 min â€” next tick should flush.
        clock.Advance(TimeSpan.FromMinutes(16));
        await sut.Generator.TickAsync(CancellationToken.None);

        await using var v2 = new OutboundDbContext(sut.OptionsByTenantId[_tenantAId]);
        (await v2.PickWaves.CountAsync()).Should().Be(1);
        (await v2.PickAssignments.CountAsync(a => a.PickWaveId == v2.PickWaves.Single().Id))
            .Should()
            .Be(5);
    }

    // â”€â”€ Test infrastructure ----------------------------------------------

    /// <summary>
    /// A tiny <see cref="TimeProvider"/> test double â€” replicates the
    /// .NET 8 <c>FakeTimeProvider</c> behaviour for <c>GetUtcNow</c> +
    /// <c>Advance</c>. We invoke <c>TickAsync</c> directly so the
    /// <see cref="PeriodicTimer"/> interaction in <c>ExecuteAsync</c>
    /// isn't tested here.
    /// </summary>
    private sealed class DeterministicTimeProvider : TimeProvider
    {
        private DateTime _now;

        public DeterministicTimeProvider(DateTime initialUtc)
        {
            _now = initialUtc;
        }

        public override DateTimeOffset GetUtcNow() => new DateTimeOffset(_now, TimeSpan.Zero);

        public void Advance(TimeSpan span) => _now = _now.Add(span);
    }

    private sealed class FakeTenantCatalog : ITenantCatalog
    {
        private readonly Dictionary<Guid, TenantInfo> _byId;
        private readonly TenantInfo[] _all;

        public FakeTenantCatalog(params TenantInfo[] tenants)
        {
            _all = tenants;
            _byId = tenants.ToDictionary(t => t.Id);
        }

        public Task<TenantInfo?> LookupBySlugAsync(string slug, CancellationToken ct) =>
            Task.FromResult<TenantInfo?>(_all.FirstOrDefault(t => t.Slug == slug));

        public Task<TenantInfo?> LookupByIdAsync(Guid tenantId, CancellationToken ct)
        {
            _byId.TryGetValue(tenantId, out var t);
            return Task.FromResult(t);
        }

        public Task<IReadOnlyList<TenantInfo>> GetReadyTenantsAsync(CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<TenantInfo>>(_all);
    }

    private sealed record Sut(
        PickWaveGeneratorService Generator,
        IServiceProvider ServiceProvider,
        Dictionary<Guid, DbContextOptions<OutboundDbContext>> OptionsByTenantId
    );

    private static Sut BuildSut(
        TimeProvider clock,
        IPickQueue queue,
        IReadOnlyList<TenantInfo> tenants,
        Guid seedPickersForTenant,
        IReadOnlyList<string> pickerIds
    )
    {
        var services = new ServiceCollection();

        services.AddSingleton<ITenantCatalog>(new FakeTenantCatalog(tenants.ToArray()));
        services.AddSingleton<IPickQueue>(queue);

        // Per-tenant in-memory DbContext options. We map each tenant id
        // to a distinct EF InMemory database name so test isolation
        // matches real per-tenant DBs.
        var optionsByTenantId = tenants.ToDictionary(
            t => t.Id,
            t =>
                new DbContextOptionsBuilder<OutboundDbContext>()
                    .UseInMemoryDatabase(t.DbName)
                    .ConfigureWarnings(w =>
                        w.Ignore(
                            Microsoft
                                .EntityFrameworkCore
                                .Diagnostics
                                .InMemoryEventId
                                .TransactionIgnoredWarning
                        )
                    )
                    .Options
        );

        // Scoped RequestContext + IRequestContext â€” the worker binds it
        // per tenant tick.
        services.AddScoped<RequestContext>();
        services.AddScoped<IRequestContext>(sp => sp.GetRequiredService<RequestContext>());

        // Per-tenant DbContext: resolve by the bound RequestContext's
        // TenantId. Real production reads from
        // IRequestContext.DbConnectionString; the in-memory test seam
        // looks up the options by tenant id.
        services.AddScoped(sp =>
        {
            var rc = sp.GetRequiredService<IRequestContext>();
            return new OutboundDbContext(optionsByTenantId[rc.TenantId]);
        });

        services.AddScoped<
            ShopFlow.Outbound.Application.Ports.IPickWaveRepository,
            ShopFlow.Outbound.Infrastructure.Repositories.PickWaveRepository
        >();
        services.AddScoped<
            ShopFlow.Outbound.Application.Ports.IPickerRepository,
            ShopFlow.Outbound.Infrastructure.Repositories.PickerRepository
        >();
        services.AddScoped<
            ShopFlow.Outbound.Application.Ports.IOrderRepository,
            ShopFlow.Outbound.Infrastructure.Repositories.OrderRepository
        >();
        services.AddScoped<
            ShopFlow.Outbound.Application.Ports.IUnitOfWork,
            ShopFlow.Outbound.Infrastructure.Repositories.OutboundUnitOfWork
        >();

        var sp = services.BuildServiceProvider();

        // Seed pickers into the per-tenant DB.
        if (pickerIds.Count > 0)
        {
            var seedOptions = optionsByTenantId[seedPickersForTenant];
            using var seedCtx = new OutboundDbContext(seedOptions);
            foreach (var pid in pickerIds)
            {
                seedCtx.Pickers.Add(Picker.Create(pid, $"Picker {pid}"));
            }
            seedCtx.SaveChanges();
        }

        var generator = new PickWaveGeneratorService(
            sp.GetRequiredService<IServiceScopeFactory>(),
            queue,
            clock,
            NullLogger<PickWaveGeneratorService>.Instance
        );

        return new Sut(generator, sp, optionsByTenantId);
    }

    /// <summary>
    /// Read every still-queued PickRequestV1 in the tenant's channel +
    /// seed a matching empty <see cref="Order"/> row, then RE-WRITE the
    /// items so the generator's drain sees them again. This is needed
    /// because the generator's <c>order.AttachToPickWave</c> call looks
    /// up the order by id; without a seeded row, the attach silently
    /// no-ops (matches production: an order missing from the DB is a
    /// data error, but the wave row + assignments still materialise).
    /// The seed step lets us assert <c>pick_wave_id</c> propagation in
    /// the integration test; here it just ensures the lookup doesn't
    /// fail spuriously.
    /// </summary>
    private static async Task SeedOrdersForQueuedItemsAsync(
        Sut sut,
        IPickQueue queue,
        Guid tenantId
    )
    {
        var reader = queue.GetReader(tenantId);
        var snapshot = new List<PickRequestV1>();
        while (reader.TryRead(out var item))
        {
            snapshot.Add(item);
        }
        if (snapshot.Count == 0)
        {
            return;
        }

        await using var seedCtx = new OutboundDbContext(sut.OptionsByTenantId[tenantId]);
        var index = 0;
        foreach (var item in snapshot)
        {
            var order = Order
                .Create(
                    $"seed-{tenantId:N}-{index++}",
                    item.ShippingProfile,
                    new[] { ("SKU-X", 1, (int?)1) }
                )
                .Value!;
            // Match the queued OrderId so the generator's FindByIdAsync hits.
            typeof(BaseEntity).GetProperty("Id")!.SetValue(order, item.OrderId);
            seedCtx.Orders.Add(order);
        }
        await seedCtx.SaveChangesAsync();

        // Re-write items so the generator's drain sees them.
        var writer = queue.GetWriter(tenantId);
        foreach (var item in snapshot)
        {
            writer.TryWrite(item);
        }
    }
}
