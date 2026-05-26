using Microsoft.EntityFrameworkCore;
using ShopFlow.StockSync.Domain.Aggregates;
using ShopFlow.StockSync.Infrastructure;
using ShopFlow.StockSync.Infrastructure.Persistence.Repositories;

namespace ShopFlow.StockSync.IntegrationTests;

/// <summary>
/// Sprint-5 plan U7 — DB-backed <c>SkuFlagRepository</c> against
/// Testcontainers Postgres. Covers the happy upsert path, the idempotent
/// no-op when the value is unchanged, the UNIQUE-23505 catch on duplicate
/// inserts, the toggle path that flips the row in place, and per-tenant
/// isolation (T1 + T2 separate DBs).
/// </summary>
/// <remarks>
/// Mirrors <c>PushLogPersistenceTests</c> verbatim: one tenant DB per
/// test, real EF Core <see cref="DbContext"/> directly against the
/// migrated schema, no harness layering.
/// </remarks>
[Trait("Category", "Integration")]
[Collection(StockSyncTenantCollection.Name)]
public sealed class SkuFlagRepositoryIntegrationTests
{
    private readonly StockSyncTenantFixture _fixture;

    public SkuFlagRepositoryIntegrationTests(StockSyncTenantFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task SetFlashSale_NewSku_InsertsOneRowWithFlagSet()
    {
        var tenant = await _fixture.ProvisionTenantAsync("skuflag-insert");
        await using var db = new StockSyncDbContext(tenant.Options);
        var repo = new SkuFlagRepository(db);

        await repo.SetFlashSaleAsync(tenant.Info.Id, "SKU-A", true, CancellationToken.None);

        await using var verify = new StockSyncDbContext(tenant.Options);
        var rows = await verify.SkuFlags.AsNoTracking().ToListAsync();
        rows.Should().HaveCount(1);
        rows[0].Sku.Should().Be("SKU-A");
        rows[0].IsFlashSale.Should().BeTrue();
    }

    [Fact]
    public async Task IsFlashSale_AfterSet_ReturnsTrue()
    {
        var tenant = await _fixture.ProvisionTenantAsync("skuflag-read");
        await using var db = new StockSyncDbContext(tenant.Options);
        var repo = new SkuFlagRepository(db);

        await repo.SetFlashSaleAsync(tenant.Info.Id, "SKU-B", true, CancellationToken.None);
        var read = await repo.IsFlashSaleAsync(tenant.Info.Id, "SKU-B", CancellationToken.None);

        read.Should().BeTrue();
    }

    [Fact]
    public async Task IsFlashSale_UnknownSku_ReturnsFalse()
    {
        var tenant = await _fixture.ProvisionTenantAsync("skuflag-unknown");
        await using var db = new StockSyncDbContext(tenant.Options);
        var repo = new SkuFlagRepository(db);

        var read = await repo.IsFlashSaleAsync(tenant.Info.Id, "SKU-NONE", CancellationToken.None);

        read.Should().BeFalse();
    }

    [Fact]
    public async Task SetFlashSale_SameValueTwice_LeavesOneRow_UpdatedAtUnchangedOnNoOp()
    {
        var tenant = await _fixture.ProvisionTenantAsync("skuflag-idempotent");
        var key = "SKU-IDEM";

        await using (var db1 = new StockSyncDbContext(tenant.Options))
        {
            var repo1 = new SkuFlagRepository(db1);
            await repo1.SetFlashSaleAsync(tenant.Info.Id, key, true, CancellationToken.None);
        }

        // Capture the updated_at after the second write so we can compare.
        DateTime? firstUpdatedAt;
        await using (var verify1 = new StockSyncDbContext(tenant.Options))
        {
            firstUpdatedAt = (await verify1.SkuFlags.AsNoTracking().SingleAsync()).UpdatedAt;
        }

        await using (var db2 = new StockSyncDbContext(tenant.Options))
        {
            var repo2 = new SkuFlagRepository(db2);
            await repo2.SetFlashSaleAsync(tenant.Info.Id, key, true, CancellationToken.None);
        }

        await using var verify2 = new StockSyncDbContext(tenant.Options);
        var rows = await verify2.SkuFlags.AsNoTracking().ToListAsync();
        rows.Should().HaveCount(1);
        rows[0].IsFlashSale.Should().BeTrue();
        // Aggregate's SetFlashSale is a no-op when value is unchanged —
        // updated_at must not advance.
        rows[0].UpdatedAt.Should().Be(firstUpdatedAt);
    }

    [Fact]
    public async Task SetFlashSale_Toggle_FlipsRowInPlace()
    {
        var tenant = await _fixture.ProvisionTenantAsync("skuflag-toggle");
        var key = "SKU-TOGGLE";

        await using (var db1 = new StockSyncDbContext(tenant.Options))
        {
            var repo = new SkuFlagRepository(db1);
            await repo.SetFlashSaleAsync(tenant.Info.Id, key, true, CancellationToken.None);
        }

        await using (var db2 = new StockSyncDbContext(tenant.Options))
        {
            var repo = new SkuFlagRepository(db2);
            await repo.SetFlashSaleAsync(tenant.Info.Id, key, false, CancellationToken.None);
        }

        await using var verify = new StockSyncDbContext(tenant.Options);
        var rows = await verify.SkuFlags.AsNoTracking().ToListAsync();
        rows.Should().HaveCount(1);
        rows[0].IsFlashSale.Should().BeFalse();
        rows[0].UpdatedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task SetFlashSale_PreExistingRowWithDifferentValue_TriggersUniqueCatchAndUpdates()
    {
        // Pre-seed a row via the raw DbContext so the repository's
        // INSERT path trips the 23505. This is the canon test for the
        // UNIQUE-23505 fallback path — without it the 23505 catch is
        // dead code.
        var tenant = await _fixture.ProvisionTenantAsync("skuflag-23505");
        var key = "SKU-23505";

        await using (var seed = new StockSyncDbContext(tenant.Options))
        {
            seed.SkuFlags.Add(SkuFlag.Create(key, isFlashSale: false));
            await seed.SaveChangesAsync();
        }

        await using (var db = new StockSyncDbContext(tenant.Options))
        {
            var repo = new SkuFlagRepository(db);
            // Different value — triggers INSERT (23505) then UPDATE.
            await repo.SetFlashSaleAsync(tenant.Info.Id, key, true, CancellationToken.None);
        }

        await using var verify = new StockSyncDbContext(tenant.Options);
        var rows = await verify.SkuFlags.AsNoTracking().ToListAsync();
        rows.Should().HaveCount(1);
        rows[0].IsFlashSale.Should().BeTrue();
    }

    [Fact]
    public async Task SetFlashSale_TenantIsolation_T1Set_T2ReadReturnsFalse()
    {
        var tenant1 = await _fixture.ProvisionTenantAsync("skuflag-iso1");
        var tenant2 = await _fixture.ProvisionTenantAsync("skuflag-iso2");

        await using (var db1 = new StockSyncDbContext(tenant1.Options))
        {
            var repo = new SkuFlagRepository(db1);
            await repo.SetFlashSaleAsync(
                tenant1.Info.Id,
                "SKU-SHARED",
                true,
                CancellationToken.None
            );
        }

        await using (var db2 = new StockSyncDbContext(tenant2.Options))
        {
            var repo = new SkuFlagRepository(db2);
            var read = await repo.IsFlashSaleAsync(
                tenant2.Info.Id,
                "SKU-SHARED",
                CancellationToken.None
            );
            read.Should()
                .BeFalse("each tenant has its own DB; T1's row must not be visible from T2");
        }
    }
}
