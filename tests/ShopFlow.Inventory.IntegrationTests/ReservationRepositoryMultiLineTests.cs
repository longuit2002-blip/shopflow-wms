using Microsoft.EntityFrameworkCore;
using Npgsql;
using ShopFlow.Inventory.Application;
using ShopFlow.Inventory.Domain;
using ShopFlow.Inventory.Infrastructure;
using ShopFlow.Inventory.Infrastructure.Repositories;

namespace ShopFlow.Inventory.IntegrationTests;

/// <summary>
/// Sprint-3-redux U3 / K10 + K11 — direct port tests of the multi-line
/// reservation methods (<see cref="ReservationRepository.TryReserveLinesAsync"/>,
/// <see cref="ReservationRepository.ReleaseLinesAsync"/>) plus
/// backwards-compat verification that the single-line
/// <see cref="ReservationRepository.TryReserveAsync"/> wrapper stamps
/// <c>order_line_id='_default'</c>.
/// </summary>
[Collection(InventoryTenantCollection.Name)]
[Trait("Category", "Integration")]
public sealed class ReservationRepositoryMultiLineTests : IAsyncLifetime
{
    private const string SkuA = "SKU-A";
    private const string SkuB = "SKU-B";
    private static readonly TimeSpan Ttl = TimeSpan.FromMinutes(15);

    private readonly InventoryTenantFixture _fx;
    private ProvisionedTenant _tenant = default!;

    public ReservationRepositoryMultiLineTests(InventoryTenantFixture fx)
    {
        _fx = fx;
    }

    public async Task InitializeAsync()
    {
        _tenant = await _fx.ProvisionTenantAsync("multi-line");
        await _fx.SeedStockAsync(_tenant, SkuA, available: 50);
        await _fx.SeedStockAsync(_tenant, SkuB, available: 30);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private (ReservationRepository Repo, InventoryDbContext Db) BuildRepo()
    {
        var db = new InventoryDbContext(_tenant.Options);
        var rc = _tenant.BuildRequestContext();
        var repo = new ReservationRepository(db, TimeProvider.System, rc);
        return (repo, db);
    }

    private static async Task<StockItem> GetStockAsync(InventoryDbContext db, string sku)
    {
        var rows = await db.StockItems.AsNoTracking().ToListAsync();
        return rows.Single(s => s.Sku.Value == sku);
    }

    [Fact]
    public async Task TryReserveLines_TwoLines_HappyPath_InsertsBothRows_AndDecrementsStock()
    {
        var (repo, db) = BuildRepo();
        await using var _ = db;

        var lines = new[]
        {
            new LineReservation(Sku.Create(SkuA), "L1", Quantity.From(10)),
            new LineReservation(Sku.Create(SkuB), "L2", Quantity.From(5)),
        };

        var result = await repo.TryReserveLinesAsync(
            "ORDER-MULTI-1",
            lines,
            Ttl,
            CancellationToken.None
        );

        result.IsSuccess.Should().BeTrue();
        result.Reservations.Should().HaveCount(2);
        result.LineOutcomes.Should().HaveCount(2);
        result.LineOutcomes.Should().OnlyContain(o => o.Status == LineOutcomeStatus.Reserved);

        var ledger = await db.Reservations.AsNoTracking()
            .Where(r => r.OrderId == "ORDER-MULTI-1")
            .ToListAsync();
        ledger.Should().HaveCount(2);
        ledger.Select(r => r.OrderLineId).Should().BeEquivalentTo(new[] { "L1", "L2" });

        var stockA = await GetStockAsync(db, SkuA);
        var stockB = await GetStockAsync(db, SkuB);
        stockA.Available.Value.Should().Be(40);
        stockA.Reserved.Value.Should().Be(10);
        stockB.Available.Value.Should().Be(25);
        stockB.Reserved.Value.Should().Be(5);

        var outboxCount = await db
            .OutboxMessages.AsNoTracking()
            .CountAsync(o => o.EventType.StartsWith("ShopFlow.Inventory.Domain.Events.StockReservedEvent"));
        outboxCount.Should().Be(2);
    }

    [Fact]
    public async Task TryReserveLines_OneLineOversells_AtomicFailure_NoSideEffects()
    {
        // Reseed SkuB to only 2 available so a qty=5 request oversells.
        await using (var c = new NpgsqlConnection(_tenant.ConnectionString))
        {
            await c.OpenAsync();
            await using var cmd = c.CreateCommand();
            cmd.CommandText = "UPDATE stock_items SET available = 2 WHERE sku = @s";
            cmd.Parameters.AddWithValue("s", SkuB);
            await cmd.ExecuteNonQueryAsync();
        }

        var (repo, db) = BuildRepo();
        await using var _ = db;

        var lines = new[]
        {
            new LineReservation(Sku.Create(SkuA), "L1", Quantity.From(10)),
            new LineReservation(Sku.Create(SkuB), "L2", Quantity.From(5)),
        };

        var result = await repo.TryReserveLinesAsync(
            "ORDER-OVERSOLD",
            lines,
            Ttl,
            CancellationToken.None
        );

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be("reservation.oversold");
        result.LineOutcomes.Should().HaveCount(2);
        result.LineOutcomes.Single(o => o.OrderLineId == "L1").Status
            .Should().Be(LineOutcomeStatus.Reserved);
        result.LineOutcomes.Single(o => o.OrderLineId == "L2").Status
            .Should().Be(LineOutcomeStatus.Oversold);

        // Atomic guarantee: zero ledger rows, zero stock change.
        var ledger = await db.Reservations.AsNoTracking()
            .CountAsync(r => r.OrderId == "ORDER-OVERSOLD");
        ledger.Should().Be(0);

        var stockA = await GetStockAsync(db, SkuA);
        var stockB = await GetStockAsync(db, SkuB);
        stockA.Available.Value.Should().Be(50);
        stockA.Reserved.Value.Should().Be(0);
        stockB.Available.Value.Should().Be(2);
        stockB.Reserved.Value.Should().Be(0);
    }

    [Fact]
    public async Task TryReserveLines_SameOrderTwice_IsIdempotent()
    {
        var (repo1, db1) = BuildRepo();
        var (repo2, db2) = BuildRepo();
        await using var _ = db1;
        await using var __ = db2;

        var lines = new[]
        {
            new LineReservation(Sku.Create(SkuA), "L1", Quantity.From(7)),
            new LineReservation(Sku.Create(SkuB), "L2", Quantity.From(3)),
        };

        var first = await repo1.TryReserveLinesAsync(
            "ORDER-IDEMP",
            lines,
            Ttl,
            CancellationToken.None
        );
        first.IsSuccess.Should().BeTrue();

        var second = await repo2.TryReserveLinesAsync(
            "ORDER-IDEMP",
            lines,
            Ttl,
            CancellationToken.None
        );
        second.IsSuccess.Should().BeTrue();

        // Second call returns the same Reservation ids (re-read from DB on 23505).
        var firstIds = first.Reservations.Select(r => r.Id).OrderBy(g => g).ToArray();
        var secondIds = second.Reservations.Select(r => r.Id).OrderBy(g => g).ToArray();
        secondIds.Should().BeEquivalentTo(firstIds);

        // Only 2 rows total — no duplicates.
        var ledger = await db1.Reservations.AsNoTracking()
            .CountAsync(r => r.OrderId == "ORDER-IDEMP");
        ledger.Should().Be(2);

        // Stock decremented exactly once.
        var stockA = await GetStockAsync(db1, SkuA);
        var stockB = await GetStockAsync(db1, SkuB);
        stockA.Available.Value.Should().Be(43); // 50 - 7
        stockB.Available.Value.Should().Be(27); // 30 - 3
    }

    [Fact]
    public async Task TryReserveLines_RedeliverWithDifferentLineSet_Returns_ExistingRows()
    {
        // Defensive scenario: orderId reused with different line set. Composite
        // UNIQUE on (order_id, "L1") catches the first line as a duplicate;
        // repository re-reads + returns the 2 existing rows. The new "L3" is
        // not inserted because the entire CTE failed atomically.
        var (repo, db) = BuildRepo();
        await using var _ = db;

        var first = await repo.TryReserveLinesAsync(
            "ORDER-DIFF",
            new[]
            {
                new LineReservation(Sku.Create(SkuA), "L1", Quantity.From(5)),
                new LineReservation(Sku.Create(SkuB), "L2", Quantity.From(3)),
            },
            Ttl,
            CancellationToken.None
        );
        first.IsSuccess.Should().BeTrue();

        var second = await repo.TryReserveLinesAsync(
            "ORDER-DIFF",
            new[]
            {
                new LineReservation(Sku.Create(SkuA), "L1", Quantity.From(5)),
                new LineReservation(Sku.Create(SkuA), "L3", Quantity.From(2)),
            },
            Ttl,
            CancellationToken.None
        );
        second.IsSuccess.Should().BeTrue();

        // Returned reservations are the existing L1+L2, not the new L3.
        second.Reservations.Should().HaveCount(2);
        second.Reservations.Select(r => r.OrderLineId).Should().BeEquivalentTo(new[] { "L1", "L2" });

        var ledger = await db.Reservations.AsNoTracking()
            .Where(r => r.OrderId == "ORDER-DIFF")
            .ToListAsync();
        ledger.Should().HaveCount(2);
        ledger.Select(r => r.OrderLineId).Should().NotContain("L3");
    }

    [Fact]
    public async Task ReleaseLinesAsync_PartialSet_OnlyReleasesRequestedLines()
    {
        var (repo, db) = BuildRepo();
        await using var _ = db;

        var lines = new[]
        {
            new LineReservation(Sku.Create(SkuA), "L1", Quantity.From(10)),
            new LineReservation(Sku.Create(SkuB), "L2", Quantity.From(4)),
        };
        var reserve = await repo.TryReserveLinesAsync(
            "ORDER-PARTIAL",
            lines,
            Ttl,
            CancellationToken.None
        );
        reserve.IsSuccess.Should().BeTrue();

        var release = await repo.ReleaseLinesAsync(
            "ORDER-PARTIAL",
            new[] { "L1" },
            CancellationToken.None
        );

        release.ReleasedLineIds.Should().BeEquivalentTo(new[] { "L1" });

        var ledger = await db.Reservations.AsNoTracking()
            .Where(r => r.OrderId == "ORDER-PARTIAL")
            .ToListAsync();
        var l1 = ledger.Single(r => r.OrderLineId == "L1");
        var l2 = ledger.Single(r => r.OrderLineId == "L2");
        l1.Status.Should().Be(ReservationStatus.Released);
        l2.Status.Should().Be(ReservationStatus.Pending);

        // SkuA restored, SkuB unchanged from the reserve.
        var stockA = await GetStockAsync(db, SkuA);
        var stockB = await GetStockAsync(db, SkuB);
        stockA.Available.Value.Should().Be(50); // 40 + 10
        stockA.Reserved.Value.Should().Be(0);
        stockB.Available.Value.Should().Be(26); // 30 - 4
        stockB.Reserved.Value.Should().Be(4);
    }

    [Fact]
    public async Task ReleaseLinesAsync_AlreadyReleased_ReturnsEmptyList()
    {
        var (repo, db) = BuildRepo();
        await using var _ = db;

        var lines = new[]
        {
            new LineReservation(Sku.Create(SkuA), "L1", Quantity.From(1)),
        };
        await repo.TryReserveLinesAsync(
            "ORDER-DBL-REL",
            lines,
            Ttl,
            CancellationToken.None
        );

        var firstRelease = await repo.ReleaseLinesAsync(
            "ORDER-DBL-REL",
            new[] { "L1" },
            CancellationToken.None
        );
        firstRelease.ReleasedLineIds.Should().BeEquivalentTo(new[] { "L1" });

        var secondRelease = await repo.ReleaseLinesAsync(
            "ORDER-DBL-REL",
            new[] { "L1" },
            CancellationToken.None
        );
        secondRelease.ReleasedLineIds.Should().BeEmpty();
    }

    [Fact]
    public async Task TryReserveAsync_SingleLineWrapper_BackwardsCompat_StampsDefaultLineId()
    {
        var (repo, db) = BuildRepo();
        await using var _ = db;

        var result = await repo.TryReserveAsync(
            Sku.Create(SkuA),
            "ORDER-LEGACY",
            Quantity.From(7),
            Ttl,
            CancellationToken.None
        );

        result.IsSuccess.Should().BeTrue();
        var row = await db.Reservations.AsNoTracking()
            .SingleAsync(r => r.OrderId == "ORDER-LEGACY");
        row.OrderLineId.Should().Be(Reservation.DefaultOrderLineId);
        row.Quantity.Value.Should().Be(7);

        var stockA = await GetStockAsync(db, SkuA);
        stockA.Available.Value.Should().Be(43);
        stockA.Reserved.Value.Should().Be(7);
    }

    [Fact]
    public async Task TryReserveAsync_SingleLineWrapper_Oversold_ReturnsCanonicalErrorCode()
    {
        var (repo, db) = BuildRepo();
        await using var _ = db;

        // Oversold against SkuA: only 50 available, request 100.
        var result = await repo.TryReserveAsync(
            Sku.Create(SkuA),
            "ORDER-OVER-WRAP",
            Quantity.From(100),
            Ttl,
            CancellationToken.None
        );

        result.IsSuccess.Should().BeFalse();
        // The single-line wrapper maps the multi-line "reservation.oversold"
        // back to the Sprint-1-redux public code so existing callers + tests
        // see no change.
        result.ErrorCode.Should().Be("reservation.insufficient_stock");
    }

    [Fact]
    public async Task TryReserveLines_SameSkuTwoLines_AggregatesQty_AndInsertsBothRows()
    {
        // Two lines on the same sku — common in real orders. The CTE
        // aggregates desired qty per sku for the availability check.
        var (repo, db) = BuildRepo();
        await using var _ = db;

        var lines = new[]
        {
            new LineReservation(Sku.Create(SkuA), "L1", Quantity.From(20)),
            new LineReservation(Sku.Create(SkuA), "L2", Quantity.From(15)),
        };

        var result = await repo.TryReserveLinesAsync(
            "ORDER-SAME-SKU",
            lines,
            Ttl,
            CancellationToken.None
        );

        result.IsSuccess.Should().BeTrue();
        result.Reservations.Should().HaveCount(2);

        var stockA = await GetStockAsync(db, SkuA);
        stockA.Available.Value.Should().Be(15); // 50 - 35
        stockA.Reserved.Value.Should().Be(35);
    }
}
