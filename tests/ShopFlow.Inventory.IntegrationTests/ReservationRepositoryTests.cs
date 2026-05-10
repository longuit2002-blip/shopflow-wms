using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using NSubstitute;
using ShopFlow.Inventory.Domain;
using ShopFlow.Inventory.Infrastructure;
using ShopFlow.Inventory.Infrastructure.Repositories;
using ShopFlow.SharedKernel.Application;

namespace ShopFlow.Inventory.IntegrationTests;

[Collection("Integration")]
[Trait("Category", "Integration")]
public sealed class ReservationRepositoryTests
{
    private readonly PostgresFixture _fixture;

    public ReservationRepositoryTests(PostgresFixture fixture)
    {
        _fixture = fixture;
    }

    private async Task<Guid> SeedStockItemAsync(int totalQuantity)
    {
        var tenantId = Guid.NewGuid();
        await using var conn = new NpgsqlConnection(_fixture.ConnectionString);
        await conn.OpenAsync();

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO stock_items
                (tenant_id, sku, id, name, category, total_qty,
                 allocated_qty, safety_threshold, created_at)
            VALUES
                (@tenant, 'SKU-001', @id, 'Test SKU', null, @qty, 0, 0, NOW());
            """;
        cmd.Parameters.AddWithValue("tenant", tenantId);
        cmd.Parameters.AddWithValue("id", Guid.NewGuid());
        cmd.Parameters.AddWithValue("qty", totalQuantity);
        await cmd.ExecuteNonQueryAsync();

        return tenantId;
    }

    private ReservationRepository BuildRepo(Guid tenantId, out InventoryDbContext db)
    {
        db = _fixture.CreateDbContext(tenantId);
        var ctx = Substitute.For<IRequestContext>();
        ctx.TenantId.Returns(tenantId);
        ctx.CorrelationId.Returns(Guid.NewGuid().ToString("N"));
        return new ReservationRepository(db, ctx, TimeProvider.System);
    }

    [Fact]
    public async Task TryReserveAsync_WithAvailableStock_Succeeds()
    {
        var tenantId = await SeedStockItemAsync(totalQuantity: 100);
        var repo = BuildRepo(tenantId, out var db);
        await using (db)
        {
            var result = await repo.TryReserveAsync(
                tenantId,
                new Sku("SKU-001"),
                qty: 5,
                orderId: Guid.NewGuid(),
                cancellationToken: CancellationToken.None
            );

            result.IsSuccess.Should().BeTrue();
            result.Value.Should().NotBe(Guid.Empty);

            var rowCount = await db
                .Reservations.IgnoreQueryFilters()
                .CountAsync(r => r.TenantId == tenantId && r.Status == ReservationStatus.Active);
            rowCount.Should().Be(1);
        }
    }

    [Fact]
    public async Task TryReserveAsync_WhenOversold_ReturnsFailure()
    {
        var tenantId = await SeedStockItemAsync(totalQuantity: 3);
        var repo = BuildRepo(tenantId, out var db);
        await using (db)
        {
            var result = await repo.TryReserveAsync(
                tenantId,
                new Sku("SKU-001"),
                qty: 100,
                orderId: Guid.NewGuid(),
                cancellationToken: CancellationToken.None
            );

            result.IsSuccess.Should().BeFalse();
            result.ErrorCode.Should().Be("OVERSOLD");
        }
    }

    [Fact]
    public async Task TryReserveAsync_TwoConcurrentForSameStock_ExactlyOneSucceeds()
    {
        // Light-weight version of the W3 5,000-concurrent gate (Plan §299).
        // Seeds 5 units; two parallel tasks each request 5; expect 1 success.
        var tenantId = await SeedStockItemAsync(totalQuantity: 5);

        async Task<bool> Reserve()
        {
            var repo = BuildRepo(tenantId, out var db);
            await using (db)
            {
                var r = await repo.TryReserveAsync(
                    tenantId,
                    new Sku("SKU-001"),
                    qty: 5,
                    orderId: Guid.NewGuid(),
                    cancellationToken: CancellationToken.None
                );
                return r.IsSuccess;
            }
        }

        var results = await Task.WhenAll(Reserve(), Reserve());

        results.Count(x => x).Should().Be(1);
        results.Count(x => !x).Should().Be(1);
    }

    [Fact]
    public async Task TryReserveAsync_DuplicateOrderId_ReturnsExisting()
    {
        var tenantId = await SeedStockItemAsync(totalQuantity: 100);
        var orderId = Guid.NewGuid();

        var repo = BuildRepo(tenantId, out var db);
        await using (db)
        {
            var first = await repo.TryReserveAsync(
                tenantId,
                new Sku("SKU-001"),
                qty: 5,
                orderId: orderId,
                cancellationToken: CancellationToken.None
            );
            first.IsSuccess.Should().BeTrue();

            var second = await repo.TryReserveAsync(
                tenantId,
                new Sku("SKU-001"),
                qty: 5,
                orderId: orderId,
                cancellationToken: CancellationToken.None
            );
            second.IsSuccess.Should().BeTrue();
            second.Value.Should().Be(first.Value);
        }
    }

    [Fact]
    public async Task ReleaseExpiredAsync_TransitionsActiveToExpired()
    {
        var tenantId = await SeedStockItemAsync(totalQuantity: 100);

        // Insert a row whose expires_at is already in the past.
        await using (var conn = new NpgsqlConnection(_fixture.ConnectionString))
        {
            await conn.OpenAsync();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                INSERT INTO reservations_ledger
                    (tenant_id, sku, id, qty, order_id, status, reserved_at, expires_at)
                VALUES
                    (@tenant, 'SKU-001', @id, 5, @order, 'Active',
                     NOW() - INTERVAL '1 hour', NOW() - INTERVAL '30 minutes');
                """;
            cmd.Parameters.AddWithValue("tenant", tenantId);
            cmd.Parameters.AddWithValue("id", Guid.NewGuid());
            cmd.Parameters.AddWithValue("order", Guid.NewGuid());
            await cmd.ExecuteNonQueryAsync();
        }

        var repo = BuildRepo(tenantId, out var db);
        await using (db)
        {
            var affected = await repo.ReleaseExpiredAsync(CancellationToken.None);
            affected.Should().BeGreaterThanOrEqualTo(1);

            var expiredCount = await db
                .Reservations.IgnoreQueryFilters()
                .CountAsync(r => r.TenantId == tenantId && r.Status == ReservationStatus.Expired);
            expiredCount.Should().BeGreaterThanOrEqualTo(1);
        }
    }
}
