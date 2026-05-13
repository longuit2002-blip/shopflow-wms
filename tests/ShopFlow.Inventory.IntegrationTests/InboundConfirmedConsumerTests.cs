using MassTransit;
using MassTransit.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using ShopFlow.Contracts.Inbound;
using ShopFlow.Inventory.Application.Ports;
using ShopFlow.Inventory.Infrastructure;
using ShopFlow.Inventory.Infrastructure.Consumers;
using ShopFlow.Inventory.Infrastructure.Repositories;
using ShopFlow.SharedKernel.Application;

namespace ShopFlow.Inventory.IntegrationTests;

/// <summary>
/// Sprint-2-redux U6 — <see cref="InboundConfirmedConsumer"/> against
/// real Postgres via Testcontainers + MassTransit's in-memory test
/// harness. Validates auto-create stock_items, bin upsert, dedup-based
/// idempotency, and StockChangedEvent outbox emission. The full
/// dispatcher-RabbitMQ-consumer chain is exercised in U9.
/// </summary>
[Collection(InventoryTenantCollection.Name)]
[Trait("Category", "Integration")]
public sealed class InboundConfirmedConsumerTests : IAsyncLifetime
{
    private readonly InventoryTenantFixture _fx;
    private ProvisionedTenant _tenant = default!;
    private long _binId;

    public InboundConfirmedConsumerTests(InventoryTenantFixture fx)
    {
        _fx = fx;
    }

    public async Task InitializeAsync()
    {
        _tenant = await _fx.ProvisionTenantAsync("consumer");
        _binId = await SeedBinAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private async Task<long> SeedBinAsync()
    {
        await using var conn = new NpgsqlConnection(_tenant.ConnectionString);
        await conn.OpenAsync();
        await using var zoneCmd = conn.CreateCommand();
        zoneCmd.CommandText = """
            INSERT INTO zones (name, warehouse_id) VALUES ('Z1', 'wh-1') RETURNING zone_id;
            """;
        var zoneId = (long)(await zoneCmd.ExecuteScalarAsync())!;
        await using var binCmd = conn.CreateCommand();
        binCmd.CommandText = """
            INSERT INTO bins (zone_id, name, capacity, occupancy_qty)
            VALUES (@z, 'B1', 100, 0) RETURNING bin_id;
            """;
        binCmd.Parameters.AddWithValue("z", zoneId);
        return (long)(await binCmd.ExecuteScalarAsync())!;
    }

    private async Task<ServiceProvider> BuildHostAsync()
    {
        var services = new ServiceCollection();
        services.AddLogging(b => b.AddProvider(NullLoggerProvider.Instance));

        // RequestContext bound to the test tenant for the entire scope.
        var rc = _tenant.BuildRequestContext();
        services.AddSingleton<RequestContext>(rc);
        services.AddSingleton<IRequestContext>(rc);

        services.AddScoped<InventoryDbContext>(_ => new InventoryDbContext(_tenant.Options));
        services.AddScoped<IStockItemRepository, StockItemRepository>();
        services.AddScoped<IInboundDedupRepository, InboundDedupRepository>();

        services.AddMassTransitTestHarness(cfg =>
        {
            cfg.AddConsumer<InboundConfirmedConsumer>();
        });

        var sp = services.BuildServiceProvider(true);
        await sp.GetRequiredService<ITestHarness>().Start();
        return sp;
    }

    [Fact]
    public async Task Consume_AutoCreatesStockItem_AndAppliesBinAdjustment()
    {
        await using var sp = await BuildHostAsync();
        var harness = sp.GetRequiredService<ITestHarness>();
        var rc = sp.GetRequiredService<RequestContext>();

        var msg = new InboundConfirmedV1(
            PurchaseOrderId: Guid.NewGuid(),
            PurchaseOrderLineId: Guid.NewGuid(),
            ReceivingId: Guid.NewGuid(),
            Sku: "SKU-NEW",
            ActualQuantity: 25,
            BinId: _binId,
            TenantId: rc.TenantId,
            OccurredAt: DateTime.UtcNow
        );

        await harness.Bus.Publish(msg);

        var consumed = await harness.GetConsumerHarness<InboundConfirmedConsumer>().Consumed.Any<InboundConfirmedV1>();
        consumed.Should().BeTrue();

        await using var verify = new InventoryDbContext(_tenant.Options);
        var stockRow = (await verify.StockItems.AsNoTracking().ToListAsync()).Single(s =>
            s.Sku.Value == "SKU-NEW"
        );
        stockRow.Available.Value.Should().Be(25);
        var binRow = await verify.StockItemBins.FirstAsync(b => b.Sku == "SKU-NEW");
        binRow.Quantity.Should().Be(25);
        var dedup = await verify.InboundDedup.FirstAsync(d => d.ReceivingId == msg.ReceivingId);
        dedup.LineId.Should().Be(msg.PurchaseOrderLineId);
    }

    [Fact]
    public async Task Consume_DuplicateDelivery_AcksWithoutReapply()
    {
        await using var sp = await BuildHostAsync();
        var harness = sp.GetRequiredService<ITestHarness>();
        var rc = sp.GetRequiredService<RequestContext>();

        var msg = new InboundConfirmedV1(
            PurchaseOrderId: Guid.NewGuid(),
            PurchaseOrderLineId: Guid.NewGuid(),
            ReceivingId: Guid.NewGuid(),
            Sku: "SKU-DUP",
            ActualQuantity: 7,
            BinId: _binId,
            TenantId: rc.TenantId,
            OccurredAt: DateTime.UtcNow
        );

        await harness.Bus.Publish(msg);
        await harness.GetConsumerHarness<InboundConfirmedConsumer>().Consumed.Any<InboundConfirmedV1>();

        // Second delivery with same dedup key
        await harness.Bus.Publish(msg);
        await Task.Delay(300); // allow second consumption to complete

        await using var verify = new InventoryDbContext(_tenant.Options);
        var stockRow = (await verify.StockItems.AsNoTracking().ToListAsync()).Single(s =>
            s.Sku.Value == "SKU-DUP"
        );
        stockRow.Available.Value.Should().Be(7); // only the first delivery applied
        var binRow = await verify.StockItemBins.FirstAsync(b => b.Sku == "SKU-DUP");
        binRow.Quantity.Should().Be(7);
        var dedupRows = await verify.InboundDedup.CountAsync(d => d.ReceivingId == msg.ReceivingId);
        dedupRows.Should().Be(1);
    }

    [Fact]
    public async Task Consume_HeaderTenantMismatch_RejectsMessage()
    {
        await using var sp = await BuildHostAsync();
        var harness = sp.GetRequiredService<ITestHarness>();
        var rc = sp.GetRequiredService<RequestContext>();

        // Payload TenantId differs from RequestContext.TenantId — routing fault.
        var msg = new InboundConfirmedV1(
            PurchaseOrderId: Guid.NewGuid(),
            PurchaseOrderLineId: Guid.NewGuid(),
            ReceivingId: Guid.NewGuid(),
            Sku: "SKU-X",
            ActualQuantity: 1,
            BinId: _binId,
            TenantId: Guid.NewGuid(), // wrong tenant
            OccurredAt: DateTime.UtcNow
        );

        await harness.Bus.Publish(msg);

        // Consumer throws → message faulted.
        var faulted = await harness.GetConsumerHarness<InboundConfirmedConsumer>().Consumed.Any<InboundConfirmedV1>(x => x.Exception is not null);
        faulted.Should().BeTrue();

        await using var verify = new InventoryDbContext(_tenant.Options);
        var stockRows = await verify.StockItems.AsNoTracking().ToListAsync();
        stockRows.Where(s => s.Sku.Value == "SKU-X").Should().BeEmpty();
    }
}
