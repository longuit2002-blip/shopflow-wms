using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using ShopFlow.SharedKernel.Application;
using ShopFlow.SharedKernel.Application.Ports;
using ShopFlow.SharedKernel.Domain;
using ShopFlow.StockSync.Application.Ports;
using ShopFlow.StockSync.Infrastructure.Persistence.Repositories;

namespace ShopFlow.StockSync.UnitTests.Caching;

/// <summary>
/// Sprint-5 plan U7 — <c>CachingSkuFlagRepository</c> behavioural contract.
/// Locks the 5-minute TTL, the eviction-on-write path, tenant isolation
/// of the cache key, and the DI scope handshake (RequestContext is bound
/// before the inner repo resolves).
/// </summary>
/// <remarks>
/// The cache is exercised against a stub <see cref="ISkuFlagRepository"/>
/// returned through the test-seam constructor — no real DbContext,
/// migrations, or Postgres container needed. Integration coverage of the
/// DB-backed inner repo lives in
/// <c>SkuFlagRepositoryIntegrationTests</c>.
/// </remarks>
public sealed class SkuFlagCacheTests
{
    private static readonly Guid TenantA = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid TenantB = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private static TenantInfo TenantInfoFor(Guid id, string slug = "acme") =>
        new(
            Id: id,
            Slug: slug,
            DbName: $"shopflow_t_{slug}",
            DbConnectionString: $"Host=localhost;Database=shopflow_t_{slug}",
            Region: "ap-southeast-1",
            Tier: "free",
            Status: TenantStatus.Ready
        );

    [Fact]
    public async Task IsFlashSale_FirstRead_HitsInnerRepoAndCachesResult()
    {
        var inner = Substitute.For<ISkuFlagRepository>();
        inner.IsFlashSaleAsync(TenantA, "SKU-X", Arg.Any<CancellationToken>()).Returns(true);
        var fixture = new CacheFixture(inner, TenantInfoFor(TenantA));

        var first = await fixture.Cache.IsFlashSaleAsync(TenantA, "SKU-X", CancellationToken.None);

        first.Should().BeTrue();
        await inner.Received(1).IsFlashSaleAsync(TenantA, "SKU-X", Arg.Any<CancellationToken>());
        fixture.Cache.CacheSize.Should().Be(1);
    }

    [Fact]
    public async Task IsFlashSale_WithinTtl_ReusesCachedValue()
    {
        var inner = Substitute.For<ISkuFlagRepository>();
        inner.IsFlashSaleAsync(TenantA, "SKU-X", Arg.Any<CancellationToken>()).Returns(true);
        var fixture = new CacheFixture(inner, TenantInfoFor(TenantA));

        _ = await fixture.Cache.IsFlashSaleAsync(TenantA, "SKU-X", CancellationToken.None);
        fixture.Clock.Advance(TimeSpan.FromMinutes(1));
        var second = await fixture.Cache.IsFlashSaleAsync(TenantA, "SKU-X", CancellationToken.None);

        second.Should().BeTrue();
        await inner.Received(1).IsFlashSaleAsync(TenantA, "SKU-X", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task IsFlashSale_AfterTtlExpires_RefreshesFromInnerRepo()
    {
        var inner = Substitute.For<ISkuFlagRepository>();
        inner.IsFlashSaleAsync(TenantA, "SKU-X", Arg.Any<CancellationToken>()).Returns(true, false); // first call -> true; second call -> false (toggled via admin)
        var fixture = new CacheFixture(inner, TenantInfoFor(TenantA));

        var first = await fixture.Cache.IsFlashSaleAsync(TenantA, "SKU-X", CancellationToken.None);
        fixture.Clock.Advance(CachingSkuFlagRepository.Ttl + TimeSpan.FromSeconds(1));
        var second = await fixture.Cache.IsFlashSaleAsync(TenantA, "SKU-X", CancellationToken.None);

        first.Should().BeTrue();
        second.Should().BeFalse();
        await inner.Received(2).IsFlashSaleAsync(TenantA, "SKU-X", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SetFlashSale_EvictsCacheEntry_NextReadGoesToInnerRepo()
    {
        var inner = Substitute.For<ISkuFlagRepository>();
        inner.IsFlashSaleAsync(TenantA, "SKU-X", Arg.Any<CancellationToken>()).Returns(true, false);
        var fixture = new CacheFixture(inner, TenantInfoFor(TenantA));

        // Populate cache with true.
        _ = await fixture.Cache.IsFlashSaleAsync(TenantA, "SKU-X", CancellationToken.None);

        // Admin flips the flag — cache must drop the stale entry.
        await fixture.Cache.SetFlashSaleAsync(TenantA, "SKU-X", false, CancellationToken.None);

        // Next read takes the DB path and gets the new value.
        var afterFlip = await fixture.Cache.IsFlashSaleAsync(
            TenantA,
            "SKU-X",
            CancellationToken.None
        );

        afterFlip.Should().BeFalse();
        await inner.Received(2).IsFlashSaleAsync(TenantA, "SKU-X", Arg.Any<CancellationToken>());
        await inner
            .Received(1)
            .SetFlashSaleAsync(TenantA, "SKU-X", false, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task IsFlashSale_DifferentTenants_CacheEntriesAreIndependent()
    {
        var inner = Substitute.For<ISkuFlagRepository>();
        inner.IsFlashSaleAsync(TenantA, "SKU-X", Arg.Any<CancellationToken>()).Returns(true);
        inner.IsFlashSaleAsync(TenantB, "SKU-X", Arg.Any<CancellationToken>()).Returns(false);

        var catalog = Substitute.For<ITenantCatalog>();
        catalog
            .LookupByIdAsync(TenantA, Arg.Any<CancellationToken>())
            .Returns(TenantInfoFor(TenantA, "tenant-a"));
        catalog
            .LookupByIdAsync(TenantB, Arg.Any<CancellationToken>())
            .Returns(TenantInfoFor(TenantB, "tenant-b"));
        var fixture = new CacheFixture(inner, catalog);

        var tenantA = await fixture.Cache.IsFlashSaleAsync(
            TenantA,
            "SKU-X",
            CancellationToken.None
        );
        var tenantB = await fixture.Cache.IsFlashSaleAsync(
            TenantB,
            "SKU-X",
            CancellationToken.None
        );

        tenantA.Should().BeTrue();
        tenantB.Should().BeFalse();
        fixture.Cache.CacheSize.Should().Be(2);

        // Second reads — both tenants hit cache (no extra DB calls).
        _ = await fixture.Cache.IsFlashSaleAsync(TenantA, "SKU-X", CancellationToken.None);
        _ = await fixture.Cache.IsFlashSaleAsync(TenantB, "SKU-X", CancellationToken.None);

        await inner.Received(1).IsFlashSaleAsync(TenantA, "SKU-X", Arg.Any<CancellationToken>());
        await inner.Received(1).IsFlashSaleAsync(TenantB, "SKU-X", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task IsFlashSale_EmptySku_ReturnsFalseWithoutTouchingInnerRepo()
    {
        var inner = Substitute.For<ISkuFlagRepository>();
        var fixture = new CacheFixture(inner, TenantInfoFor(TenantA));

        var result = await fixture.Cache.IsFlashSaleAsync(TenantA, "", CancellationToken.None);

        result.Should().BeFalse();
        await inner
            .DidNotReceive()
            .IsFlashSaleAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task IsFlashSale_UnknownTenant_Throws()
    {
        var inner = Substitute.For<ISkuFlagRepository>();
        var catalog = Substitute.For<ITenantCatalog>();
        catalog
            .LookupByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns((TenantInfo?)null);
        var fixture = new CacheFixture(inner, catalog);

        var act = () => fixture.Cache.IsFlashSaleAsync(TenantA, "SKU-X", CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task SetFlashSale_BindsTenantInScopeBeforeInnerCall()
    {
        // Proves the scope is bound to the right tenant before the inner
        // repo executes — the recorded slug captured via the slug probe
        // inside the innerResolver should match the tenant we asked for.
        var inner = Substitute.For<ISkuFlagRepository>();
        var holder = new SlugHolder();
        var fixture = new CacheFixture(
            inner,
            TenantInfoFor(TenantA, "tenant-a"),
            slugHolder: holder
        );

        await fixture.Cache.SetFlashSaleAsync(TenantA, "SKU-X", true, CancellationToken.None);

        holder.LastSlug.Should().Be("tenant-a");
        await inner
            .Received(1)
            .SetFlashSaleAsync(TenantA, "SKU-X", true, Arg.Any<CancellationToken>());
    }

    // ----------------------------------------------------------------------
    // Fixture + test doubles
    // ----------------------------------------------------------------------

    private sealed class SlugHolder
    {
        public string? LastSlug { get; set; }
    }

    private sealed class CacheFixture
    {
        public CachingSkuFlagRepository Cache { get; }
        public TestTimeProvider Clock { get; } = new();

        public CacheFixture(
            ISkuFlagRepository inner,
            TenantInfo tenant,
            SlugHolder? slugHolder = null
        )
            : this(inner, BuildSingleTenantCatalog(tenant), slugHolder) { }

        public CacheFixture(
            ISkuFlagRepository inner,
            ITenantCatalog catalog,
            SlugHolder? slugHolder = null
        )
        {
            var services = new ServiceCollection();
            services.AddScoped<RequestContext>();
            services.AddScoped<IRequestContext>(sp => sp.GetRequiredService<RequestContext>());
            var sp = services.BuildServiceProvider();
            var scopeFactory = sp.GetRequiredService<IServiceScopeFactory>();

            Cache = new CachingSkuFlagRepository(
                scopeFactory,
                catalog,
                Clock,
                NullLogger<CachingSkuFlagRepository>.Instance,
                innerResolver: scopeSp =>
                {
                    if (slugHolder is not null)
                    {
                        // Read the request context the wrapper just bound and
                        // record the slug so the test can assert the tenant
                        // binding sequence is correct.
                        var rc = scopeSp.GetRequiredService<IRequestContext>();
                        slugHolder.LastSlug = rc.TenantSlug;
                    }
                    return inner;
                }
            );
        }

        private static ITenantCatalog BuildSingleTenantCatalog(TenantInfo tenant)
        {
            var catalog = Substitute.For<ITenantCatalog>();
            catalog.LookupByIdAsync(tenant.Id, Arg.Any<CancellationToken>()).Returns(tenant);
            return catalog;
        }
    }

    private sealed class TestTimeProvider : TimeProvider
    {
        private DateTimeOffset _now = new(2026, 5, 17, 9, 0, 0, TimeSpan.Zero);

        public void Advance(TimeSpan by) => _now = _now.Add(by);

        public override DateTimeOffset GetUtcNow() => _now;
    }
}
