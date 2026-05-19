using Microsoft.EntityFrameworkCore;
using Npgsql;
using ShopFlow.Inventory.Application.Ports;
using ShopFlow.Inventory.Domain.Catalog;
using ShopFlow.Inventory.Domain.Catalog.ValueObjects;
using ShopFlow.Inventory.Infrastructure;
using ShopFlow.Inventory.Infrastructure.Repositories;
using SkuCode = ShopFlow.Inventory.Domain.Sku;

namespace ShopFlow.Inventory.IntegrationTests.Repositories;

/// <summary>
/// Sprint-7.5 U3 — integration coverage for <see cref="SkuRepository"/>
/// against real Postgres via the shared <see cref="InventoryTenantFixture"/>.
/// Validates the upsert + flash-sale-toggle + partial-index +
/// partial-UNIQUE-barcode semantics promised by the migration.
/// </summary>
[Collection(InventoryTenantCollection.Name)]
[Trait("Category", "Integration")]
public sealed class SkuRepositoryTests : IAsyncLifetime
{
    private readonly InventoryTenantFixture _fx;
    private ProvisionedTenant _tenant = default!;

    public SkuRepositoryTests(InventoryTenantFixture fx)
    {
        _fx = fx;
    }

    public async Task InitializeAsync()
    {
        _tenant = await _fx.ProvisionTenantAsync("sku");
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private SkuRepository BuildRepo(InventoryDbContext db) => new(db);

    private static Sku NewSku(
        string code,
        string name = "Test",
        int? threshold = null,
        string? barcode = null,
        bool isFlashSale = false,
        string? category = null)
    {
        var result = Sku.Create(
            code: SkuCode.Create(code),
            name: name,
            category: category,
            threshold: threshold,
            barcode: barcode,
            isFlashSale: isFlashSale
        );
        result.IsSuccess.Should().BeTrue();
        return result.Value!;
    }

    [Fact]
    public async Task GetByIdAsync_Miss_ReturnsNull()
    {
        await using var db = new InventoryDbContext(_tenant.Options);
        var repo = BuildRepo(db);

        var row = await repo.GetByIdAsync(SkuCode.Create("MISS"), CancellationToken.None);

        row.Should().BeNull();
    }

    [Fact]
    public async Task UpsertAsync_NewSku_InsertsRow()
    {
        await using var db = new InventoryDbContext(_tenant.Options);
        var repo = BuildRepo(db);

        var result = await repo.UpsertAsync(
            NewSku("SKU-INS", name: "Inserted", threshold: 10),
            CancellationToken.None
        );

        result.Changed.Should().BeTrue();
        result.Sku.Threshold.Should().Be(10);

        await using var verify = new InventoryDbContext(_tenant.Options);
        var row = await verify.Skus.AsNoTracking().FirstOrDefaultAsync(s => s.Code == SkuCode.Create("SKU-INS"));
        row.Should().NotBeNull();
        row!.Name.Should().Be("Inserted");
        row.Threshold.Should().Be(10);
    }

    [Fact]
    public async Task UpsertAsync_ExistingModified_UpdatesAndReturnsChanged()
    {
        await using var db1 = new InventoryDbContext(_tenant.Options);
        await BuildRepo(db1).UpsertAsync(
            NewSku("SKU-MOD", name: "Old", threshold: 5),
            CancellationToken.None
        );

        await using var db2 = new InventoryDbContext(_tenant.Options);
        var repo = BuildRepo(db2);

        var result = await repo.UpsertAsync(
            NewSku("SKU-MOD", name: "New", threshold: 8),
            CancellationToken.None
        );

        result.Changed.Should().BeTrue();

        await using var verify = new InventoryDbContext(_tenant.Options);
        var row = await verify.Skus.AsNoTracking().FirstAsync(s => s.Code == SkuCode.Create("SKU-MOD"));
        row.Name.Should().Be("New");
        row.Threshold.Should().Be(8);
    }

    [Fact]
    public async Task UpsertAsync_ExistingUnchanged_ReturnsNotChanged()
    {
        await using var db1 = new InventoryDbContext(_tenant.Options);
        await BuildRepo(db1).UpsertAsync(
            NewSku("SKU-NOOP", name: "Same", threshold: 5),
            CancellationToken.None
        );

        await using var db2 = new InventoryDbContext(_tenant.Options);
        var repo = BuildRepo(db2);

        var result = await repo.UpsertAsync(
            NewSku("SKU-NOOP", name: "Same", threshold: 5),
            CancellationToken.None
        );

        result.Changed.Should().BeFalse();
    }

    [Fact]
    public async Task UpdateFlashSaleAsync_OnExisting_TogglesAndReportsChanged()
    {
        await using var db1 = new InventoryDbContext(_tenant.Options);
        await BuildRepo(db1).UpsertAsync(NewSku("SKU-FS"), CancellationToken.None);

        await using var db2 = new InventoryDbContext(_tenant.Options);
        var result = await BuildRepo(db2).UpdateFlashSaleAsync(
            SkuCode.Create("SKU-FS"),
            active: true,
            CancellationToken.None
        );

        result.IsSuccess.Should().BeTrue();
        result.Value!.Changed.Should().BeTrue();
        result.Value.Sku.IsFlashSale.Should().BeTrue();
    }

    [Fact]
    public async Task UpdateFlashSaleAsync_AlreadyAtRequestedValue_ReportsNotChanged()
    {
        await using var db1 = new InventoryDbContext(_tenant.Options);
        await BuildRepo(db1).UpsertAsync(
            NewSku("SKU-FS2", isFlashSale: true),
            CancellationToken.None
        );

        await using var db2 = new InventoryDbContext(_tenant.Options);
        var result = await BuildRepo(db2).UpdateFlashSaleAsync(
            SkuCode.Create("SKU-FS2"),
            active: true,
            CancellationToken.None
        );

        result.IsSuccess.Should().BeTrue();
        // U5 reads Changed=false to skip the outbox emit on idempotent retries.
        result.Value!.Changed.Should().BeFalse();
    }

    [Fact]
    public async Task UpdateFlashSaleAsync_MissingRow_AutoCreatesMinimal()
    {
        await using var db = new InventoryDbContext(_tenant.Options);

        var result = await BuildRepo(db).UpdateFlashSaleAsync(
            SkuCode.Create("SKU-NEW-FS"),
            active: true,
            CancellationToken.None
        );

        result.IsSuccess.Should().BeTrue();
        result.Value!.Changed.Should().BeTrue();
        result.Value.Sku.IsFlashSale.Should().BeTrue();

        await using var verify = new InventoryDbContext(_tenant.Options);
        var row = await verify.Skus.AsNoTracking().FirstOrDefaultAsync(s => s.Code == SkuCode.Create("SKU-NEW-FS"));
        row.Should().NotBeNull();
        row!.Name.Should().Be("SKU-NEW-FS");
        row.IsFlashSale.Should().BeTrue();
    }

    [Fact]
    public async Task UpdateThresholdAsync_MissingRow_AutoCreatesMinimal()
    {
        await using var db = new InventoryDbContext(_tenant.Options);

        var result = await BuildRepo(db).UpdateThresholdAsync(
            SkuCode.Create("SKU-NEW-T"),
            threshold: 5,
            CancellationToken.None
        );

        result.IsSuccess.Should().BeTrue();
        result.Value!.Changed.Should().BeTrue();

        await using var verify = new InventoryDbContext(_tenant.Options);
        var row = await verify.Skus.AsNoTracking().FirstOrDefaultAsync(s => s.Code == SkuCode.Create("SKU-NEW-T"));
        row!.Threshold.Should().Be(5);
    }

    [Fact]
    public async Task PartialUniqueBarcode_TwoNullBarcodes_BothAccepted()
    {
        await using var db1 = new InventoryDbContext(_tenant.Options);
        var a = await BuildRepo(db1).UpsertAsync(NewSku("BC-A", barcode: null), CancellationToken.None);
        a.Changed.Should().BeTrue();

        await using var db2 = new InventoryDbContext(_tenant.Options);
        var b = await BuildRepo(db2).UpsertAsync(NewSku("BC-B", barcode: null), CancellationToken.None);
        b.Changed.Should().BeTrue();
    }

    [Fact]
    public async Task PartialUniqueBarcode_DuplicateNonNull_ThrowsPostgres23505()
    {
        await using var db1 = new InventoryDbContext(_tenant.Options);
        await BuildRepo(db1).UpsertAsync(NewSku("BC-X", barcode: "1234"), CancellationToken.None);

        await using var db2 = new InventoryDbContext(_tenant.Options);
        var act = async () => await BuildRepo(db2).UpsertAsync(
            NewSku("BC-Y", barcode: "1234"),
            CancellationToken.None
        );

        var ex = await act.Should().ThrowAsync<DbUpdateException>();
        var pg = ex.Which.InnerException as PostgresException;
        pg.Should().NotBeNull();
        pg!.SqlState.Should().Be("23505");
    }

    [Fact]
    public async Task GetListMetadataAsync_BulkLookup_ReturnsOnlyMatchingRows()
    {
        await using var db = new InventoryDbContext(_tenant.Options);
        var repo = BuildRepo(db);
        await repo.UpsertAsync(NewSku("BLK-1", name: "n1", category: "c1", threshold: 5), CancellationToken.None);
        await repo.UpsertAsync(NewSku("BLK-2", name: "n2", isFlashSale: true), CancellationToken.None);

        await using var db2 = new InventoryDbContext(_tenant.Options);
        var meta = await BuildRepo(db2).GetListMetadataAsync(
            new[] { "BLK-1", "BLK-2", "BLK-MISS" },
            CancellationToken.None
        );

        meta.Should().HaveCount(2);
        meta["BLK-1"].Threshold.Should().Be(5);
        meta["BLK-1"].Category.Should().Be("c1");
        meta["BLK-2"].IsFlashSale.Should().BeTrue();
        meta.ContainsKey("BLK-MISS").Should().BeFalse();
    }

    [Fact]
    public async Task GetAllThresholdsAsync_OnlyReturnsRowsWithThresholdSet()
    {
        await using var db = new InventoryDbContext(_tenant.Options);
        var repo = BuildRepo(db);
        await repo.UpsertAsync(NewSku("T-A", threshold: 10), CancellationToken.None);
        await repo.UpsertAsync(NewSku("T-B"), CancellationToken.None);
        await repo.UpsertAsync(NewSku("T-C", threshold: 0), CancellationToken.None);

        await using var db2 = new InventoryDbContext(_tenant.Options);
        var t = await BuildRepo(db2).GetAllThresholdsAsync(CancellationToken.None);

        t.Should().HaveCount(2);
        t["T-A"].Should().Be(10);
        t["T-C"].Should().Be(0);
        t.ContainsKey("T-B").Should().BeFalse();
    }

    [Fact]
    public async Task Migration_AppliesAndCreatesExpectedIndexes()
    {
        // Inspect pg_indexes for the three production indexes shipped
        // in 20260519000001_AddSkusRichCatalog. Sprint-5 has precedent
        // for pg_indexes smoke checks.
        await using var conn = new NpgsqlConnection(_tenant.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT indexname
            FROM pg_indexes
            WHERE tablename = 'skus'
            ORDER BY indexname
            """;

        var names = new List<string>();
        await using (var rdr = await cmd.ExecuteReaderAsync())
        {
            while (await rdr.ReadAsync())
            {
                names.Add(rdr.GetString(0));
            }
        }

        names.Should().Contain("pk_skus");
        names.Should().Contain("ix_skus_category");
        names.Should().Contain("ix_skus_is_flash_sale");
        names.Should().Contain("ux_skus_barcode");

        // Partial-index predicate sanity — pg_indexes.indexdef carries
        // the WHERE clause for partial indexes.
        await using var defCmd = conn.CreateCommand();
        defCmd.CommandText = """
            SELECT indexname, indexdef
            FROM pg_indexes
            WHERE tablename = 'skus' AND indexname IN ('ix_skus_is_flash_sale', 'ux_skus_barcode')
            """;
        var defs = new Dictionary<string, string>();
        await using (var rdr = await defCmd.ExecuteReaderAsync())
        {
            while (await rdr.ReadAsync())
            {
                defs[rdr.GetString(0)] = rdr.GetString(1);
            }
        }

        defs["ix_skus_is_flash_sale"].Should().Contain("WHERE", "is_flash_sale partial index must carry a predicate");
        defs["ix_skus_is_flash_sale"].ToLowerInvariant().Should().Contain("is_flash_sale");
        defs["ux_skus_barcode"].ToLowerInvariant().Should().Contain("barcode");
        defs["ux_skus_barcode"].Should().Contain("WHERE", "barcode partial UNIQUE must carry a predicate");
    }

    [Fact]
    public async Task LargeCatalog_CategoryFilter_UsesBtreeIndex()
    {
        // Seed ~1.2k rows split across 3 categories so the planner has
        // enough cardinality to pick the index over a seq-scan. The
        // 10k-row scale from the plan is a CI-friendly target; we use
        // 1.2k here to keep dev-CI run-time low while still surfacing
        // index-vs-seq-scan via EXPLAIN.
        await using var db = new InventoryDbContext(_tenant.Options);
        var repo = BuildRepo(db);

        var categories = new[] { "alpha", "beta", "gamma" };
        for (var i = 0; i < 1200; i++)
        {
            await repo.UpsertAsync(
                NewSku($"BULK-{i:D5}", name: $"Item {i}", category: categories[i % categories.Length]),
                CancellationToken.None
            );
        }

        // ANALYZE so the planner has fresh stats before the EXPLAIN.
        await using (var ana = new NpgsqlConnection(_tenant.ConnectionString))
        {
            await ana.OpenAsync();
            await using var c = ana.CreateCommand();
            c.CommandText = "ANALYZE skus";
            await c.ExecuteNonQueryAsync();
        }

        // EXPLAIN — accept either an index-scan or bitmap-index-scan
        // path on ix_skus_category; the planner picks based on stats.
        await using var explain = new NpgsqlConnection(_tenant.ConnectionString);
        await explain.OpenAsync();
        await using var cmd = explain.CreateCommand();
        cmd.CommandText = "EXPLAIN SELECT sku FROM skus WHERE category = 'alpha'";
        var plan = new System.Text.StringBuilder();
        await using (var rdr = await cmd.ExecuteReaderAsync())
        {
            while (await rdr.ReadAsync())
            {
                plan.AppendLine(rdr.GetString(0));
            }
        }

        var planText = plan.ToString();
        planText.Should().Contain("ix_skus_category", "category filter must use the btree index, not seq-scan");
    }
}
