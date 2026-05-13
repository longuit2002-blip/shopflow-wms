using Microsoft.EntityFrameworkCore;
using Npgsql;
using ShopFlow.Inventory.Domain;
using ShopFlow.Inventory.Infrastructure;
using ShopFlow.Inventory.Infrastructure.Repositories;
using ShopFlow.SharedKernel.Infrastructure;

namespace ShopFlow.Inventory.IntegrationTests;

/// <summary>
/// Sprint-1-redux U1 + U2: hot-path <see cref="ReservationRepository"/>
/// against real Postgres. Each test class provisions a fresh tenant DB so
/// state from one test never leaks into another.
/// </summary>
[Collection(InventoryTenantCollection.Name)]
[Trait("Category", "Integration")]
public sealed class ReservationRepositoryTests : IAsyncLifetime
{
    private const string Sku100 = "SKU-100";
    private static readonly TimeSpan Ttl = TimeSpan.FromMinutes(15);

    private readonly InventoryTenantFixture _fx;
    private ProvisionedTenant _tenant = default!;

    public ReservationRepositoryTests(InventoryTenantFixture fx)
    {
        _fx = fx;
    }

    public async Task InitializeAsync()
    {
        _tenant = await _fx.ProvisionTenantAsync("repo");
        await _fx.SeedStockAsync(_tenant, Sku100, available: 100);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private async Task<(ReservationRepository Repo, InventoryDbContext Db)> BuildRepoAsync()
    {
        var db = new InventoryDbContext(_tenant.Options);
        var rc = _tenant.BuildRequestContext();
        var repo = new ReservationRepository(db, TimeProvider.System, rc);
        await Task.CompletedTask;
        return (repo, db);
    }

    /// <summary>
    /// Fetch a single <see cref="StockItem"/> by SKU string. EF can't
    /// translate <c>s.Sku.Value == "..."</c> because <see cref="Sku"/> is a
    /// value-object property under <c>HasConversion</c>, and its
    /// <c>operator ==</c> overload (from <c>ValueObject</c>) doesn't
    /// translate either. The test fixture has at most a handful of rows
    /// per tenant DB, so materialising once and filtering in C# is both
    /// correct and cheap.
    /// </summary>
    private static async Task<StockItem> GetStockAsync(InventoryDbContext db, string sku)
    {
        var rows = await db.StockItems.AsNoTracking().ToListAsync();
        return rows.Single(s => s.Sku.Value == sku);
    }

    [Fact]
    public async Task TryReserve_HappyPath_CreatesPendingRow_AndEmitsOutbox()
    {
        var (repo, db) = await BuildRepoAsync();
        await using var _ = db;

        var result = await repo.TryReserveAsync(
            Sku.Create(Sku100),
            "ORDER-1",
            Quantity.From(10),
            Ttl,
            CancellationToken.None
        );

        result.IsSuccess.Should().BeTrue();
        result.Value!.Status.Should().Be(ReservationStatus.Pending);
        result.Value.Quantity.Value.Should().Be(10);

        var stockRow = await GetStockAsync(db, Sku100);
        stockRow.Available.Value.Should().Be(90);
        stockRow.Reserved.Value.Should().Be(10);

        var outbox = await db
            .OutboxMessages.AsNoTracking()
            .Where(o => o.EventType.StartsWith("ShopFlow.Inventory.Domain.Events.StockReservedEvent"))
            .CountAsync();
        outbox.Should().Be(1);
    }

    [Fact]
    public async Task TryReserve_QtyEqualsAvailable_Succeeds()
    {
        var (repo, db) = await BuildRepoAsync();
        await using var _ = db;

        var result = await repo.TryReserveAsync(
            Sku.Create(Sku100),
            "ORDER-EXACT",
            Quantity.From(100),
            Ttl,
            CancellationToken.None
        );

        result.IsSuccess.Should().BeTrue();

        var stockRow = await GetStockAsync(db, Sku100);
        stockRow.Available.Value.Should().Be(0);
        stockRow.Reserved.Value.Should().Be(100);
    }

    [Fact]
    public async Task TryReserve_QtyOverAvailable_ReturnsOversold_NoStockChange()
    {
        var (repo, db) = await BuildRepoAsync();
        await using var _ = db;

        var result = await repo.TryReserveAsync(
            Sku.Create(Sku100),
            "ORDER-OVER",
            Quantity.From(101),
            Ttl,
            CancellationToken.None
        );

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be("reservation.insufficient_stock");

        var stockRow = await GetStockAsync(db, Sku100);
        stockRow.Available.Value.Should().Be(100);
        stockRow.Reserved.Value.Should().Be(0);

        var ledgerRows = await db.Reservations.AsNoTracking().CountAsync();
        ledgerRows.Should().Be(0);
    }

    [Fact]
    public async Task TryReserve_SameOrderIdTwice_ReturnsSameId_OneLedgerRow()
    {
        var (repo1, db1) = await BuildRepoAsync();
        var (repo2, db2) = await BuildRepoAsync();

        await using (db1)
        await using (db2)
        {
            var first = await repo1.TryReserveAsync(
                Sku.Create(Sku100),
                "ORDER-IDEMP",
                Quantity.From(5),
                Ttl,
                CancellationToken.None
            );
            var second = await repo2.TryReserveAsync(
                Sku.Create(Sku100),
                "ORDER-IDEMP",
                Quantity.From(5),
                Ttl,
                CancellationToken.None
            );

            first.IsSuccess.Should().BeTrue();
            second.IsSuccess.Should().BeTrue();
            second.Value!.Id.Should().Be(first.Value!.Id);

            var ledgerCount = await db1.Reservations.CountAsync(r => r.OrderId == "ORDER-IDEMP");
            ledgerCount.Should().Be(1);

            var stockRow = await GetStockAsync(db1, Sku100);
            stockRow.Available.Value.Should().Be(95);
            stockRow.Reserved.Value.Should().Be(5);
        }
    }

    [Fact]
    public async Task TryReserve_ConcurrentOversell_AtMostAvailableSucceed()
    {
        await _fx.SeedStockAsync(_tenant, "HOT-1000", available: 1000);
        var (repo0, db0) = await BuildRepoAsync();
        await using var __ = db0;

        // 30 concurrent callers each requesting qty=60 against available=1000.
        // Expect exactly 16 successes (16 × 60 = 960 ≤ 1000, 17 × 60 = 1020 > 1000).
        const int callers = 30;
        const int qtyEach = 60;
        var orderIds = Enumerable.Range(0, callers).Select(i => $"BURST-{i:D4}").ToArray();

        var tasks = orderIds
            .Select(async oid =>
            {
                var db = new InventoryDbContext(_tenant.Options);
                var repo = new ReservationRepository(
                    db,
                    TimeProvider.System,
                    _tenant.BuildRequestContext()
                );
                try
                {
                    return await repo.TryReserveAsync(
                        Sku.Create("HOT-1000"),
                        oid,
                        Quantity.From(qtyEach),
                        Ttl,
                        CancellationToken.None
                    );
                }
                finally
                {
                    await db.DisposeAsync();
                }
            })
            .ToArray();

        var results = await Task.WhenAll(tasks);

        var successCount = results.Count(r => r.IsSuccess);
        var oversoldCount = results.Count(r =>
            !r.IsSuccess && r.ErrorCode == "reservation.insufficient_stock"
        );

        (successCount + oversoldCount).Should().Be(callers);
        successCount.Should().BeLessThanOrEqualTo(16);

        var stockRow = await GetStockAsync(db0, "HOT-1000");
        // Available + Reserved must equal initial total — invariant of TryReserve.
        (stockRow.Available.Value + stockRow.Reserved.Value).Should().Be(1000);
        // Reserved must equal successes × qtyEach.
        stockRow.Reserved.Value.Should().Be(successCount * qtyEach);
    }

    [Fact]
    public async Task FindByOrderId_AfterTryReserve_ReturnsRow()
    {
        var (repo, db) = await BuildRepoAsync();
        await using var _ = db;

        await repo.TryReserveAsync(
            Sku.Create(Sku100),
            "ORDER-FIND",
            Quantity.From(3),
            Ttl,
            CancellationToken.None
        );

        var found = await repo.FindByOrderIdAsync("ORDER-FIND", CancellationToken.None);
        found.Should().NotBeNull();
        found!.Quantity.Value.Should().Be(3);
        found.Status.Should().Be(ReservationStatus.Pending);
    }

    [Fact]
    public async Task FindByOrderId_UnknownOrder_ReturnsNull()
    {
        var (repo, db) = await BuildRepoAsync();
        await using var _ = db;

        var found = await repo.FindByOrderIdAsync("GHOST", CancellationToken.None);
        found.Should().BeNull();
    }

    [Fact]
    public async Task Confirm_OnPending_FlipsToConfirmed_DecrementsReserved()
    {
        var (repo, db) = await BuildRepoAsync();
        await using var _ = db;
        await repo.TryReserveAsync(
            Sku.Create(Sku100),
            "ORDER-CONFIRM",
            Quantity.From(7),
            Ttl,
            CancellationToken.None
        );

        var result = await repo.ConfirmAsync("ORDER-CONFIRM", CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        var row = await db.Reservations.AsNoTracking().FirstAsync(r => r.OrderId == "ORDER-CONFIRM");
        row.Status.Should().Be(ReservationStatus.Confirmed);
        row.ConfirmedAt.Should().NotBeNull();

        var stock = await GetStockAsync(db, Sku100);
        stock.Available.Value.Should().Be(93);
        stock.Reserved.Value.Should().Be(0);

        var changeEvents = await db
            .OutboxMessages.AsNoTracking()
            .CountAsync(o => o.EventType.StartsWith("ShopFlow.Inventory.Domain.Events.StockChangedEvent"));
        changeEvents.Should().Be(1);
    }

    [Fact]
    public async Task Confirm_OnAlreadyConfirmed_ReturnsAlreadyConfirmed()
    {
        var (repo, db) = await BuildRepoAsync();
        await using var _ = db;
        await repo.TryReserveAsync(
            Sku.Create(Sku100),
            "ORDER-DBL",
            Quantity.From(1),
            Ttl,
            CancellationToken.None
        );
        await repo.ConfirmAsync("ORDER-DBL", CancellationToken.None);

        var second = await repo.ConfirmAsync("ORDER-DBL", CancellationToken.None);

        second.IsSuccess.Should().BeFalse();
        second.ErrorCode.Should().Be("reservation.already_confirmed");
    }

    [Fact]
    public async Task Confirm_NonExistentOrder_ReturnsNotFound()
    {
        var (repo, db) = await BuildRepoAsync();
        await using var _ = db;

        var result = await repo.ConfirmAsync("DOES-NOT-EXIST", CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be("reservation.not_found");
    }

    [Fact]
    public async Task Release_OnPending_FlipsToReleased_RestoresAvailable()
    {
        var (repo, db) = await BuildRepoAsync();
        await using var _ = db;
        await repo.TryReserveAsync(
            Sku.Create(Sku100),
            "ORDER-REL",
            Quantity.From(8),
            Ttl,
            CancellationToken.None
        );

        var result = await repo.ReleaseAsync("ORDER-REL", CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        var stock = await GetStockAsync(db, Sku100);
        stock.Available.Value.Should().Be(100);
        stock.Reserved.Value.Should().Be(0);
    }

    [Fact]
    public async Task ReleaseExpired_FlipsExpiredRows_RestoresAvailable_EmitsEventsPerRow()
    {
        var (repo, db) = await BuildRepoAsync();
        await using var _ = db;

        // Seed 3 already-expired reservations directly via SQL so we don't have
        // to wait for the real TTL clock to tick past.
        var pastNow = DateTime.UtcNow.AddMinutes(-30);
        var pastExpires = pastNow.AddMinutes(-1);
        for (var i = 0; i < 3; i++)
        {
            await InsertPendingReservationDirectAsync(
                db,
                Guid.NewGuid(),
                Sku100,
                $"OLD-{i}",
                quantity: 5,
                createdAt: pastNow,
                expiresAt: pastExpires
            );
        }
        // Bump the materialized stock_items.reserved to mirror the seed.
        await using (var bump = new NpgsqlConnection(_tenant.ConnectionString))
        {
            await bump.OpenAsync();
            await using var cmd = bump.CreateCommand();
            cmd.CommandText =
                "UPDATE stock_items SET reserved = reserved + 15, available = available - 15 WHERE sku = @sku";
            cmd.Parameters.AddWithValue("sku", Sku100);
            await cmd.ExecuteNonQueryAsync();
        }

        var released = await repo.ReleaseExpiredAsync(DateTime.UtcNow, 100, CancellationToken.None);

        released.Should().Be(3);

        var stock = await GetStockAsync(db, Sku100);
        stock.Available.Value.Should().Be(100);
        stock.Reserved.Value.Should().Be(0);

        var expiredRows = await db
            .Reservations.AsNoTracking()
            .CountAsync(r => r.Status == ReservationStatus.Expired);
        expiredRows.Should().Be(3);

        var releaseEvents = await db
            .OutboxMessages.AsNoTracking()
            .CountAsync(o => o.EventType.StartsWith("ShopFlow.Inventory.Domain.Events.StockReleasedEvent"));
        releaseEvents.Should().Be(3);
    }

    [Fact]
    public async Task ReleaseExpired_NoEligibleRows_Returns0_NoEvents()
    {
        var (repo, db) = await BuildRepoAsync();
        await using var _ = db;
        await repo.TryReserveAsync(
            Sku.Create(Sku100),
            "ORDER-FRESH",
            Quantity.From(2),
            Ttl,
            CancellationToken.None
        );

        var released = await repo.ReleaseExpiredAsync(DateTime.UtcNow, 100, CancellationToken.None);

        released.Should().Be(0);
        var stock = await GetStockAsync(db, Sku100);
        stock.Reserved.Value.Should().Be(2);
        var releaseEvents = await db
            .OutboxMessages.AsNoTracking()
            .CountAsync(o => o.EventType.StartsWith("ShopFlow.Inventory.Domain.Events.StockReleasedEvent"));
        releaseEvents.Should().Be(0);
    }

    private static async Task InsertPendingReservationDirectAsync(
        InventoryDbContext db,
        Guid id,
        string sku,
        string orderId,
        int quantity,
        DateTime createdAt,
        DateTime expiresAt
    )
    {
        // Open a fresh standalone connection — `await using` on
        // db.Database.GetDbConnection() would dispose EF's owned connection
        // and break the surrounding DbContext.
        await using var conn = new NpgsqlConnection(db.Database.GetConnectionString());
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO reservations_ledger
                (id, sku, order_id, quantity, status, expires_at, created_at)
            VALUES (@id, @sku, @order, @qty, 'Pending', @expires, @created)
            """;
        cmd.Parameters.AddWithValue("id", id);
        cmd.Parameters.AddWithValue("sku", sku);
        cmd.Parameters.AddWithValue("order", orderId);
        cmd.Parameters.AddWithValue("qty", quantity);
        cmd.Parameters.AddWithValue("expires", expiresAt);
        cmd.Parameters.AddWithValue("created", createdAt);
        await cmd.ExecuteNonQueryAsync();
    }
}
