using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using NSubstitute;
using ShopFlow.Inventory.Application.Commands;
using ShopFlow.Inventory.Application.Handlers;
using ShopFlow.Inventory.Domain;
using ShopFlow.Inventory.Infrastructure;
using ShopFlow.Inventory.Infrastructure.Repositories;
using ShopFlow.SharedKernel.Application;
using ShopFlow.SharedKernel.Infrastructure;

namespace ShopFlow.Inventory.IntegrationTests;

[Collection("Integration")]
[Trait("Category", "Integration")]
public sealed class OutboxIntegrationTests
{
    private readonly PostgresFixture _fixture;

    public OutboxIntegrationTests(PostgresFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task AdjustStockHandler_WritesOutboxRowAtomicallyWithBusinessWrite()
    {
        var tenantId = Guid.NewGuid();
        await SeedStockItem(tenantId);

        var ctx = Substitute.For<IRequestContext>();
        ctx.TenantId.Returns(tenantId);
        ctx.CorrelationId.Returns(Guid.NewGuid().ToString("N"));

        // Construct a DbContext that wires the OutboxInterceptor exactly
        // as the production composition does — the kernel's interceptor
        // is what turns the StockItem.AdjustStock domain event into a row
        // in outbox_messages within the same transaction.
        var outboxInterceptor = new OutboxInterceptor();
        var tenancyInterceptor = new TenancyInterceptor(ctx);

        var options = new DbContextOptionsBuilder<InventoryDbContext>()
            .UseNpgsql(_fixture.ConnectionString)
            .AddInterceptors(tenancyInterceptor, outboxInterceptor)
            .Options;

        await using (var db = new InventoryDbContext(options, ctx))
        {
            var stockItems = new StockItemRepository(db, TimeProvider.System);
            var unitOfWork = new InventoryUnitOfWork(db);
            var handler = new AdjustStockHandler(stockItems, unitOfWork, ctx);

            var result = await handler.Handle(
                new AdjustStockCommand(
                    "SKU-001",
                    +5,
                    StockAdjustmentReason.Receiving,
                    Guid.NewGuid()
                ),
                CancellationToken.None
            );
            result.IsSuccess.Should().BeTrue();
        }

        // Verify exactly one outbox row landed for this tenant.
        await using var assertDb = _fixture.CreateDbContext(tenantId);
        var outboxRows = await assertDb
            .OutboxMessages.IgnoreQueryFilters()
            .Where(m => m.TenantId == tenantId)
            .ToListAsync();
        outboxRows.Should().HaveCount(1);
        outboxRows[0].EventType.Should().Contain("StockAdjustedEvent");
    }

    private async Task SeedStockItem(Guid tenantId)
    {
        await using var conn = new NpgsqlConnection(_fixture.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO stock_items
                (tenant_id, sku, id, name, category, total_qty,
                 allocated_qty, safety_threshold, created_at)
            VALUES
                (@tenant, 'SKU-001', @id, 'Test', null, 50, 0, 0, NOW());
            """;
        cmd.Parameters.AddWithValue("tenant", tenantId);
        cmd.Parameters.AddWithValue("id", Guid.NewGuid());
        await cmd.ExecuteNonQueryAsync();
    }
}
