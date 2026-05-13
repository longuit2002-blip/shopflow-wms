using System.Text.Json;
using MassTransit;
using MassTransit.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using ShopFlow.Contracts.Inventory;
using ShopFlow.Inventory.Application.Ports;
using ShopFlow.Inventory.Infrastructure;
using ShopFlow.Inventory.Infrastructure.Consumers;
using ShopFlow.Inventory.Infrastructure.Repositories;
using ShopFlow.SharedKernel.Application;

namespace ShopFlow.Inventory.IntegrationTests;

/// <summary>
/// Sprint-3-redux U3 — <see cref="ReserveStockConsumer"/> against real
/// Postgres via Testcontainers + MassTransit's in-memory test harness.
/// Validates the all-or-nothing multi-line CTE, atomic failure surfacing
/// per-line outcomes, idempotency on redelivery, and defense-in-depth
/// tenant mismatch rejection.
/// </summary>
[Collection(InventoryTenantCollection.Name)]
[Trait("Category", "Integration")]
public sealed class ReserveStockConsumerTests : IAsyncLifetime
{
    private readonly InventoryTenantFixture _fx;
    private ProvisionedTenant _tenant = default!;

    public ReserveStockConsumerTests(InventoryTenantFixture fx)
    {
        _fx = fx;
    }

    public async Task InitializeAsync()
    {
        _tenant = await _fx.ProvisionTenantAsync("reserve-cons");
        await _fx.SeedStockAsync(_tenant, "SKU-A", available: 50);
        await _fx.SeedStockAsync(_tenant, "SKU-B", available: 10);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private async Task<ServiceProvider> BuildHostAsync()
    {
        var services = new ServiceCollection();
        services.AddLogging(b => b.AddProvider(NullLoggerProvider.Instance));

        var rc = _tenant.BuildRequestContext();
        services.AddSingleton<RequestContext>(rc);
        services.AddSingleton<IRequestContext>(rc);
        services.AddSingleton<TimeProvider>(TimeProvider.System);

        services.AddScoped<InventoryDbContext>(_ => new InventoryDbContext(_tenant.Options));
        services.AddScoped<IReservationRepository, ReservationRepository>();

        services.AddMassTransitTestHarness(cfg => cfg.AddConsumer<ReserveStockConsumer>());

        var sp = services.BuildServiceProvider(true);
        await sp.GetRequiredService<ITestHarness>().Start();
        return sp;
    }

    [Fact]
    public async Task Consume_TwoLineHappyPath_EmitsStockReservedV1WithBothOutcomes()
    {
        await using var sp = await BuildHostAsync();
        var harness = sp.GetRequiredService<ITestHarness>();
        var rc = sp.GetRequiredService<RequestContext>();

        var orderId = Guid.NewGuid();
        var msg = new ReserveStockV1(
            OrderId: orderId,
            TenantId: rc.TenantId,
            Lines: new[]
            {
                new ReserveStockLineV1("L1", "SKU-A", 10),
                new ReserveStockLineV1("L2", "SKU-B", 5),
            },
            Ttl: TimeSpan.FromMinutes(15)
        );

        await harness.Bus.Publish(msg);
        var consumed = await harness
            .GetConsumerHarness<ReserveStockConsumer>()
            .Consumed.Any<ReserveStockV1>();
        consumed.Should().BeTrue();

        // Assert outbox row of the canonical contract type.
        await using var verify = new InventoryDbContext(_tenant.Options);
        var outboxRows = await verify
            .OutboxMessages.AsNoTracking()
            .Where(o => o.EventType.StartsWith("ShopFlow.Contracts.Inventory.StockReservedV1"))
            .ToListAsync();
        outboxRows.Should().HaveCount(1);
        var row = outboxRows.Single();
        using var doc = JsonDocument.Parse(row.Payload);
        doc.RootElement.GetProperty("orderId").GetGuid().Should().Be(orderId);
        doc.RootElement.GetProperty("lineOutcomes").GetArrayLength().Should().Be(2);

        // Ledger holds 2 rows for the order.
        var ledger = await verify.Reservations.AsNoTracking()
            .Where(r => r.OrderId == orderId.ToString())
            .ToListAsync();
        ledger.Should().HaveCount(2);
    }

    [Fact]
    public async Task Consume_AtomicOversold_EmitsStockReservationFailedV1_NoLedgerWrite()
    {
        await using var sp = await BuildHostAsync();
        var harness = sp.GetRequiredService<ITestHarness>();
        var rc = sp.GetRequiredService<RequestContext>();

        var orderId = Guid.NewGuid();
        // SKU-B has 10 available; requesting 999 oversells. SKU-A would pass.
        var msg = new ReserveStockV1(
            OrderId: orderId,
            TenantId: rc.TenantId,
            Lines: new[]
            {
                new ReserveStockLineV1("L1", "SKU-A", 10),
                new ReserveStockLineV1("L2", "SKU-B", 999),
            },
            Ttl: TimeSpan.FromMinutes(15)
        );

        await harness.Bus.Publish(msg);
        await harness.GetConsumerHarness<ReserveStockConsumer>().Consumed.Any<ReserveStockV1>();

        await using var verify = new InventoryDbContext(_tenant.Options);
        var failedRows = await verify
            .OutboxMessages.AsNoTracking()
            .Where(o =>
                o.EventType.StartsWith("ShopFlow.Contracts.Inventory.StockReservationFailedV1")
            )
            .ToListAsync();
        failedRows.Should().HaveCount(1);
        using var doc = JsonDocument.Parse(failedRows.Single().Payload);
        var outcomes = doc.RootElement.GetProperty("lineOutcomes").EnumerateArray().ToArray();
        outcomes.Should().HaveCount(2);
        outcomes
            .Single(e => e.GetProperty("orderLineId").GetString() == "L1")
            .GetProperty("status")
            .GetString()
            .Should()
            .Be("Reserved");
        outcomes
            .Single(e => e.GetProperty("orderLineId").GetString() == "L2")
            .GetProperty("status")
            .GetString()
            .Should()
            .Be("Oversold");

        // Atomic: no ledger rows for the order.
        var ledger = await verify.Reservations.AsNoTracking()
            .CountAsync(r => r.OrderId == orderId.ToString());
        ledger.Should().Be(0);

        // Stock unchanged.
        var stockA = (await verify.StockItems.AsNoTracking().ToListAsync())
            .Single(s => s.Sku.Value == "SKU-A");
        var stockB = (await verify.StockItems.AsNoTracking().ToListAsync())
            .Single(s => s.Sku.Value == "SKU-B");
        stockA.Available.Value.Should().Be(50);
        stockB.Available.Value.Should().Be(10);
    }

    [Fact]
    public async Task Consume_Redelivery_PublishesEventTwice_LedgerStaysOnePerLine()
    {
        await using var sp = await BuildHostAsync();
        var harness = sp.GetRequiredService<ITestHarness>();
        var rc = sp.GetRequiredService<RequestContext>();

        var orderId = Guid.NewGuid();
        var msg = new ReserveStockV1(
            OrderId: orderId,
            TenantId: rc.TenantId,
            Lines: new[]
            {
                new ReserveStockLineV1("L1", "SKU-A", 5),
            },
            Ttl: TimeSpan.FromMinutes(15)
        );

        await harness.Bus.Publish(msg);
        await harness.GetConsumerHarness<ReserveStockConsumer>().Consumed.Any<ReserveStockV1>();

        await harness.Bus.Publish(msg);
        await Task.Delay(300); // allow second consumption

        await using var verify = new InventoryDbContext(_tenant.Options);
        var ledgerCount = await verify.Reservations.AsNoTracking()
            .CountAsync(r => r.OrderId == orderId.ToString());
        ledgerCount.Should().Be(1); // idempotency via composite UNIQUE

        // Consumer is idempotent at the repo, so two reserve events emit.
        var outboxCount = await verify
            .OutboxMessages.AsNoTracking()
            .CountAsync(o => o.EventType.StartsWith("ShopFlow.Contracts.Inventory.StockReservedV1"));
        outboxCount.Should().Be(2);
    }

    [Fact]
    public async Task Consume_TenantMismatch_FaultsToDlq()
    {
        await using var sp = await BuildHostAsync();
        var harness = sp.GetRequiredService<ITestHarness>();

        var msg = new ReserveStockV1(
            OrderId: Guid.NewGuid(),
            TenantId: Guid.NewGuid(), // wrong tenant — does not match RequestContext binding
            Lines: new[]
            {
                new ReserveStockLineV1("L1", "SKU-A", 1),
            },
            Ttl: TimeSpan.FromMinutes(15)
        );

        await harness.Bus.Publish(msg);
        var faulted = await harness
            .GetConsumerHarness<ReserveStockConsumer>()
            .Consumed.Any<ReserveStockV1>(x => x.Exception is not null);
        faulted.Should().BeTrue();

        await using var verify = new InventoryDbContext(_tenant.Options);
        var rows = await verify.Reservations.AsNoTracking()
            .CountAsync(r => r.OrderId == msg.OrderId.ToString());
        rows.Should().Be(0);
    }
}
