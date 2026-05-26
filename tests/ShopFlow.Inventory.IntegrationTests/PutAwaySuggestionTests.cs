using Microsoft.EntityFrameworkCore;
using Npgsql;
using ShopFlow.Inventory.Application.Ports;
using ShopFlow.Inventory.Domain;
using ShopFlow.Inventory.Infrastructure;
using ShopFlow.Inventory.Infrastructure.Services;

namespace ShopFlow.Inventory.IntegrationTests;

/// <summary>
/// Sprint-2-redux U5 — <see cref="PutAwaySuggestionService"/> ranking
/// validation. Seeds zones + bins with known (capacity, occupancy)
/// patterns and asserts the top-K ordering per plan R16.
/// </summary>
[Collection(InventoryTenantCollection.Name)]
[Trait("Category", "Integration")]
public sealed class PutAwaySuggestionTests : IAsyncLifetime
{
    private readonly InventoryTenantFixture _fx;
    private ProvisionedTenant _tenant = default!;

    public PutAwaySuggestionTests(InventoryTenantFixture fx)
    {
        _fx = fx;
    }

    public async Task InitializeAsync()
    {
        _tenant = await _fx.ProvisionTenantAsync("putaway");
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private async Task<(long ZoneId, long BinId)> SeedBinAsync(
        string zoneName,
        string binName,
        int capacity,
        int occupancy
    )
    {
        await using var conn = new NpgsqlConnection(_tenant.ConnectionString);
        await conn.OpenAsync();
        await using var zoneCmd = conn.CreateCommand();
        zoneCmd.CommandText = """
            INSERT INTO zones (name, warehouse_id) VALUES (@n, 'wh-1')
            ON CONFLICT DO NOTHING;
            SELECT zone_id FROM zones WHERE name = @n;
            """;
        zoneCmd.Parameters.AddWithValue("n", zoneName);
        var zoneId = (long)(await zoneCmd.ExecuteScalarAsync())!;

        await using var binCmd = conn.CreateCommand();
        binCmd.CommandText = """
            INSERT INTO bins (zone_id, name, capacity, occupancy_qty)
            VALUES (@z, @n, @cap, @occ)
            RETURNING bin_id;
            """;
        binCmd.Parameters.AddWithValue("z", zoneId);
        binCmd.Parameters.AddWithValue("n", binName);
        binCmd.Parameters.AddWithValue("cap", capacity);
        binCmd.Parameters.AddWithValue("occ", occupancy);
        var binId = (long)(await binCmd.ExecuteScalarAsync())!;
        return (zoneId, binId);
    }

    [Fact]
    public async Task TopCandidates_RanksByAvailableCapacityDesc()
    {
        // 3 bins same zone — capacities (100, 80), (100, 20), (100, 50) per AE5.
        await SeedBinAsync("Z1", "B-A", 100, 80);
        await SeedBinAsync("Z1", "B-B", 100, 20);
        await SeedBinAsync("Z1", "B-C", 100, 50);

        await using var db = new InventoryDbContext(_tenant.Options);
        var svc = new PutAwaySuggestionService(db);

        var top = await svc.GetTopCandidatesAsync(
            "SKU-A",
            requestedQty: 10,
            topK: 3,
            ct: CancellationToken.None
        );

        top.Should().HaveCount(3);
        top[0].BinName.Should().Be("B-B"); // avail 80
        top[1].BinName.Should().Be("B-C"); // avail 50
        top[2].BinName.Should().Be("B-A"); // avail 20
    }

    [Fact]
    public async Task TopCandidates_FiltersOutBinsBelowRequestedQty()
    {
        await SeedBinAsync("Z1", "B-FULL", 10, 10); // available=0
        await SeedBinAsync("Z1", "B-OK", 100, 0);

        await using var db = new InventoryDbContext(_tenant.Options);
        var svc = new PutAwaySuggestionService(db);

        var top = await svc.GetTopCandidatesAsync("SKU-X", 50, 3, CancellationToken.None);

        top.Should().HaveCount(1);
        top[0].BinName.Should().Be("B-OK");
    }

    [Fact]
    public async Task TopCandidates_HomeZoneRanksFirst()
    {
        var (homeZoneId, _) = await SeedBinAsync("Z-HOME", "B-HOME", 100, 50);
        await SeedBinAsync("Z-OTHER", "B-OTHER", 100, 0); // higher capacity

        // Set the SKU's home zone.
        await using (var conn = new NpgsqlConnection(_tenant.ConnectionString))
        {
            await conn.OpenAsync();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                INSERT INTO stock_items (sku, available, reserved, home_zone_id, created_at, row_version)
                VALUES ('SKU-HOMED', 0, 0, @z, NOW(), (txid_current())::text::xid);
                """;
            cmd.Parameters.AddWithValue("z", homeZoneId);
            await cmd.ExecuteNonQueryAsync();
        }

        await using var db = new InventoryDbContext(_tenant.Options);
        var svc = new PutAwaySuggestionService(db);
        var top = await svc.GetTopCandidatesAsync("SKU-HOMED", 10, 3, CancellationToken.None);

        top[0].BinName.Should().Be("B-HOME");
        top[0].IsHomeZone.Should().BeTrue();
        top[1].BinName.Should().Be("B-OTHER");
        top[1].IsHomeZone.Should().BeFalse();
    }

    [Fact]
    public async Task TopCandidates_TiebreakerByBinNameLexAsc()
    {
        await SeedBinAsync("Z1", "B-Z", 100, 30);
        await SeedBinAsync("Z1", "B-A", 100, 30);
        await SeedBinAsync("Z1", "B-M", 100, 30);

        await using var db = new InventoryDbContext(_tenant.Options);
        var svc = new PutAwaySuggestionService(db);
        var top = await svc.GetTopCandidatesAsync("SKU-T", 10, 3, CancellationToken.None);

        top.Select(c => c.BinName).Should().ContainInOrder(new[] { "B-A", "B-M", "B-Z" });
    }
}
