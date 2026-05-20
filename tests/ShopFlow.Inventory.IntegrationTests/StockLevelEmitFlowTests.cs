using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using ShopFlow.Contracts.Inventory;
using ShopFlow.Inventory.Application;
using ShopFlow.Inventory.Application.Ports;
using ShopFlow.Inventory.Domain;
using ShopFlow.Inventory.Infrastructure;
using ShopFlow.Inventory.Infrastructure.Repositories;
using ShopFlow.SharedKernel.Infrastructure;

namespace ShopFlow.Inventory.IntegrationTests;

/// <summary>
/// Sprint-5 U2 / KTD1 — verifies the Inventory side emits the canonical
/// <see cref="StockLevelChangedV1"/> cross-module event from every
/// stock-mutating repository path. The StockSync engine (Sprint-5 U3+)
/// consumes this event downstream.
/// </summary>
[Collection(InventoryTenantCollection.Name)]
[Trait("Category", "Integration")]
public sealed class StockLevelEmitFlowTests : IAsyncLifetime
{
    private const string Sku100 = "SKU-100";
    private const string Sku200 = "SKU-200";
    private const string EventTypePrefix = "ShopFlow.Contracts.Inventory.StockLevelChangedV1";

    private readonly InventoryTenantFixture _fx;
    private ProvisionedTenant _tenant = default!;

    public StockLevelEmitFlowTests(InventoryTenantFixture fx)
    {
        _fx = fx;
    }

    public async Task InitializeAsync()
    {
        _tenant = await _fx.ProvisionTenantAsync("emit");
        await _fx.SeedStockAsync(_tenant, Sku100, available: 100);
        await _fx.SeedStockAsync(_tenant, Sku200, available: 50);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private (ReservationRepository Repo, InventoryDbContext Db) BuildRepo()
    {
        var db = new InventoryDbContext(_tenant.Options);
        var repo = new ReservationRepository(
            db,
            TimeProvider.System,
            _tenant.BuildRequestContext()
        );
        return (repo, db);
    }

    private static async Task<List<StockLevelChangedV1>> ReadV1RowsAsync(InventoryDbContext db)
    {
        var rows = await db
            .OutboxMessages.AsNoTracking()
            .Where(o => o.EventType.StartsWith(EventTypePrefix))
            .OrderBy(o => o.CreatedAt)
            .ToListAsync();
        return rows
            .Select(o => JsonSerializer.Deserialize<StockLevelChangedV1>(
                o.Payload,
                OutboxJsonOptions.Default
            )!)
            .ToList();
    }

    [Fact]
    public async Task Reserve_SingleSku_EmitsOneV1WithPostCommitAvailable()
    {
        var (repo, db) = BuildRepo();
        await using var _ = db;

        await repo.TryReserveAsync(
            Sku.Create(Sku100),
            "ORDER-1",
            Quantity.From(10),
            TimeSpan.FromMinutes(15),
            CancellationToken.None
        );

        var v1 = await ReadV1RowsAsync(db);
        v1.Should().HaveCount(1);
        v1[0].Sku.Should().Be(Sku100);
        v1[0].AvailableToSell.Should().Be(90);
        v1[0].TenantId.Should().Be(_tenant.BuildRequestContext().TenantId);
    }

    [Fact]
    public async Task Reserve_MultiSku_EmitsOneV1PerUniqueSku()
    {
        var (repo, db) = BuildRepo();
        await using var _ = db;

        var lines = new[]
        {
            new LineReservation(Sku.Create(Sku100), "L1", Quantity.From(5)),
            new LineReservation(Sku.Create(Sku200), "L2", Quantity.From(20)),
        };
        await repo.TryReserveLinesAsync(
            "ORDER-MULTI",
            lines,
            TimeSpan.FromMinutes(15),
            CancellationToken.None
        );

        var v1 = await ReadV1RowsAsync(db);
        v1.Should().HaveCount(2);
        v1.Single(e => e.Sku == Sku100).AvailableToSell.Should().Be(95);
        v1.Single(e => e.Sku == Sku200).AvailableToSell.Should().Be(30);
    }

    [Fact]
    public async Task Confirm_EmitsV1PerSku_AfterReserve()
    {
        var (repo, db) = BuildRepo();
        await using var _ = db;

        await repo.TryReserveAsync(
            Sku.Create(Sku100),
            "ORDER-CONFIRM",
            Quantity.From(7),
            TimeSpan.FromMinutes(15),
            CancellationToken.None
        );
        await repo.ConfirmAsync("ORDER-CONFIRM", CancellationToken.None);

        var v1 = await ReadV1RowsAsync(db);
        // 1 from reserve + 1 from confirm
        v1.Should().HaveCount(2);
        v1[0].AvailableToSell.Should().Be(93);
        v1[1].AvailableToSell.Should().Be(93); // confirm doesn't change available
    }

    [Fact]
    public async Task Release_EmitsV1PerSku_RestoringAvailable()
    {
        var (repo, db) = BuildRepo();
        await using var _ = db;

        await repo.TryReserveAsync(
            Sku.Create(Sku100),
            "ORDER-REL",
            Quantity.From(15),
            TimeSpan.FromMinutes(15),
            CancellationToken.None
        );
        await repo.ReleaseAsync("ORDER-REL", CancellationToken.None);

        var v1 = await ReadV1RowsAsync(db);
        v1.Should().HaveCount(2);
        v1[0].AvailableToSell.Should().Be(85); // after reserve
        v1[1].AvailableToSell.Should().Be(100); // after release
    }

    [Fact]
    public async Task IdempotentRelease_EmitsNoV1_WhenNothingMatched()
    {
        var (repo, db) = BuildRepo();
        await using var _ = db;

        // Release on an order that never existed → idempotent no-op
        await repo.ReleaseLinesAsync(
            "ORDER-NEVER",
            new[] { "L1" },
            CancellationToken.None
        );

        var v1 = await ReadV1RowsAsync(db);
        v1.Should().BeEmpty(
            "idempotent no-op release must not emit StockLevelChangedV1 — nothing changed"
        );
    }

    [Fact]
    public async Task PayloadDeserializesCleanly_WithCanonicalShape()
    {
        var (repo, db) = BuildRepo();
        await using var _ = db;

        await repo.TryReserveAsync(
            Sku.Create(Sku100),
            "ORDER-PAYLOAD",
            Quantity.From(3),
            TimeSpan.FromMinutes(15),
            CancellationToken.None
        );

        var raw = await db
            .OutboxMessages.AsNoTracking()
            .Where(o => o.EventType.StartsWith(EventTypePrefix))
            .Select(o => o.Payload)
            .SingleAsync();

        var deserialized = JsonSerializer.Deserialize<StockLevelChangedV1>(
            raw,
            OutboxJsonOptions.Default
        );
        deserialized.Should().NotBeNull();
        deserialized!.Sku.Should().Be(Sku100);
        deserialized.AvailableToSell.Should().Be(97);
    }
}
