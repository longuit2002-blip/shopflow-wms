using Microsoft.EntityFrameworkCore;
using Npgsql;
using ShopFlow.Inventory.Domain;
using ShopFlow.Inventory.Infrastructure;
using ShopFlow.Inventory.Infrastructure.Repositories;

namespace ShopFlow.Inventory.IntegrationTests;

/// <summary>
/// Sprint-2-redux U5 — bin-aware <see cref="StockItemRepository.AdjustAtBinAsync"/>
/// against real Postgres. Validates UPSERT semantics on stock_items +
/// stock_item_bins, bin underflow protection, audit row, and bin
/// occupancy update.
/// </summary>
[Collection(InventoryTenantCollection.Name)]
[Trait("Category", "Integration")]
public sealed class StockItemRepositoryAdjustTests : IAsyncLifetime
{
    private readonly InventoryTenantFixture _fx;
    private ProvisionedTenant _tenant = default!;

    public StockItemRepositoryAdjustTests(InventoryTenantFixture fx)
    {
        _fx = fx;
    }

    public async Task InitializeAsync()
    {
        _tenant = await _fx.ProvisionTenantAsync("adj");
        await SeedZoneAndBinAsync(zoneName: "Z1", binName: "B1", capacity: 100);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private async Task<long> SeedZoneAndBinAsync(string zoneName, string binName, int capacity)
    {
        await using var conn = new NpgsqlConnection(_tenant.ConnectionString);
        await conn.OpenAsync();
        await using var zoneCmd = conn.CreateCommand();
        zoneCmd.CommandText = """
            INSERT INTO zones (name, warehouse_id) VALUES (@n, 'wh-1') RETURNING zone_id;
            """;
        zoneCmd.Parameters.AddWithValue("n", zoneName);
        var zoneId = (long)(await zoneCmd.ExecuteScalarAsync())!;

        await using var binCmd = conn.CreateCommand();
        binCmd.CommandText = """
            INSERT INTO bins (zone_id, name, capacity, occupancy_qty)
            VALUES (@z, @n, @cap, 0)
            RETURNING bin_id;
            """;
        binCmd.Parameters.AddWithValue("z", zoneId);
        binCmd.Parameters.AddWithValue("n", binName);
        binCmd.Parameters.AddWithValue("cap", capacity);
        var binId = (long)(await binCmd.ExecuteScalarAsync())!;
        return binId;
    }

    private async Task<long> FindBinIdAsync(string binName)
    {
        await using var conn = new NpgsqlConnection(_tenant.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT bin_id FROM bins WHERE name = @n";
        cmd.Parameters.AddWithValue("n", binName);
        return (long)(await cmd.ExecuteScalarAsync())!;
    }

    [Fact]
    public async Task AdjustAtBin_UnknownSku_AutoCreatesStockItemAndBinRow()
    {
        var binId = await FindBinIdAsync("B1");
        await using var db = new InventoryDbContext(_tenant.Options);
        var repo = new StockItemRepository(db, _tenant.BuildRequestContext());

        var result = await repo.AdjustAtBinAsync(
            Sku.Create("SKU-NEW"),
            binId,
            +10,
            StockAdjustmentReason.Receipt,
            note: "PO test",
            ct: CancellationToken.None
        );

        result.IsSuccess.Should().BeTrue();

        await using var verify = new InventoryDbContext(_tenant.Options);
        var rows = await verify.StockItems.AsNoTracking().ToListAsync();
        var stockRow = rows.Single(s => s.Sku.Value == "SKU-NEW");
        stockRow.Available.Value.Should().Be(10);
        stockRow.Reserved.Value.Should().Be(0);

        var binRow = await verify.StockItemBins.FirstAsync(b => b.BinId == binId);
        binRow.Quantity.Should().Be(10);

        var binOccupancy = await verify.Bins.FirstAsync(b => b.BinId == binId);
        binOccupancy.OccupancyQty.Should().Be(10);

        var auditCount = await verify.StockAdjustments.CountAsync();
        auditCount.Should().Be(1);
    }

    [Fact]
    public async Task AdjustAtBin_ExistingSkuAndBin_IncrementsQuantity()
    {
        var binId = await FindBinIdAsync("B1");
        await using var db = new InventoryDbContext(_tenant.Options);
        var repo = new StockItemRepository(db, _tenant.BuildRequestContext());
        await repo.AdjustAtBinAsync(
            Sku.Create("SKU-EXIST"),
            binId,
            +5,
            StockAdjustmentReason.Receipt,
            null,
            CancellationToken.None
        );

        await using var db2 = new InventoryDbContext(_tenant.Options);
        var repo2 = new StockItemRepository(db2, _tenant.BuildRequestContext());
        var result = await repo2.AdjustAtBinAsync(
            Sku.Create("SKU-EXIST"),
            binId,
            +3,
            StockAdjustmentReason.Receipt,
            null,
            CancellationToken.None
        );

        result.IsSuccess.Should().BeTrue();

        await using var verify = new InventoryDbContext(_tenant.Options);
        var stockRow = (await verify.StockItems.AsNoTracking().ToListAsync()).Single(s =>
            s.Sku.Value == "SKU-EXIST"
        );
        stockRow.Available.Value.Should().Be(8);
        var binRow = await verify.StockItemBins.FirstAsync(b => b.Sku == "SKU-EXIST");
        binRow.Quantity.Should().Be(8);
    }

    [Fact]
    public async Task AdjustAtBin_NegativeUnderflow_FailsAndRollsBack()
    {
        var binId = await FindBinIdAsync("B1");
        await using var db = new InventoryDbContext(_tenant.Options);
        var repo = new StockItemRepository(db, _tenant.BuildRequestContext());
        await repo.AdjustAtBinAsync(
            Sku.Create("SKU-UF"),
            binId,
            +5,
            StockAdjustmentReason.Receipt,
            null,
            CancellationToken.None
        );

        await using var db2 = new InventoryDbContext(_tenant.Options);
        var repo2 = new StockItemRepository(db2, _tenant.BuildRequestContext());
        var result = await repo2.AdjustAtBinAsync(
            Sku.Create("SKU-UF"),
            binId,
            -10,
            StockAdjustmentReason.Damage,
            null,
            CancellationToken.None
        );

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be("stock.bin_underflow");

        await using var verify = new InventoryDbContext(_tenant.Options);
        var binRow = await verify.StockItemBins.FirstAsync(b => b.Sku == "SKU-UF");
        binRow.Quantity.Should().Be(5);
        var stockRow = (await verify.StockItems.AsNoTracking().ToListAsync()).Single(s =>
            s.Sku.Value == "SKU-UF"
        );
        stockRow.Available.Value.Should().Be(5);
    }

    [Fact]
    public async Task AdjustAtBin_ZeroDelta_FailsWithCode()
    {
        var binId = await FindBinIdAsync("B1");
        await using var db = new InventoryDbContext(_tenant.Options);
        var repo = new StockItemRepository(db, _tenant.BuildRequestContext());

        var result = await repo.AdjustAtBinAsync(
            Sku.Create("SKU-Z"),
            binId,
            0,
            StockAdjustmentReason.Receipt,
            null,
            CancellationToken.None
        );

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be("stock.adjustment_zero");
    }
}
