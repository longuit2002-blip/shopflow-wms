using MassTransit;
using MassTransit.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using ShopFlow.Contracts.Inventory;
using ShopFlow.Inventory.Application;
using ShopFlow.Inventory.Application.Ports;
using ShopFlow.Inventory.Domain;
using ShopFlow.Inventory.Infrastructure;
using ShopFlow.Inventory.Infrastructure.Consumers;
using ShopFlow.Inventory.Infrastructure.Repositories;
using ShopFlow.SharedKernel.Application;

namespace ShopFlow.Inventory.IntegrationTests;

/// <summary>
/// Sprint-3-redux U3 — <see cref="ConfirmStockConsumer"/> tests. Covers
/// happy-path multi-line confirm + idempotent already-confirmed
/// re-delivery.
/// </summary>
[Collection(InventoryTenantCollection.Name)]
[Trait("Category", "Integration")]
public sealed class ConfirmStockConsumerTests : IAsyncLifetime
{
    private readonly InventoryTenantFixture _fx;
    private ProvisionedTenant _tenant = default!;

    public ConfirmStockConsumerTests(InventoryTenantFixture fx)
    {
        _fx = fx;
    }

    public async Task InitializeAsync()
    {
        _tenant = await _fx.ProvisionTenantAsync("confirm-cons");
        await _fx.SeedStockAsync(_tenant, "SKU-A", available: 50);
        await _fx.SeedStockAsync(_tenant, "SKU-B", available: 30);
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

        services.AddMassTransitTestHarness(cfg => cfg.AddConsumer<ConfirmStockConsumer>());

        var sp = services.BuildServiceProvider(true);
        await sp.GetRequiredService<ITestHarness>().Start();
        return sp;
    }

    private async Task SeedMultiLineReservationAsync(Guid orderId)
    {
        await using var db = new InventoryDbContext(_tenant.Options);
        var repo = new ReservationRepository(db, TimeProvider.System, _tenant.BuildRequestContext());
        var lines = new[]
        {
            new LineReservation(Sku.Create("SKU-A"), "L1", Quantity.From(8)),
            new LineReservation(Sku.Create("SKU-B"), "L2", Quantity.From(4)),
        };
        var r = await repo.TryReserveLinesAsync(
            orderId.ToString(),
            lines,
            TimeSpan.FromMinutes(15),
            CancellationToken.None
        );
        r.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task Consume_HappyPath_ConfirmsAllRows_EmitsStockConfirmedV1()
    {
        var orderId = Guid.NewGuid();
        await SeedMultiLineReservationAsync(orderId);

        await using var sp = await BuildHostAsync();
        var harness = sp.GetRequiredService<ITestHarness>();
        var rc = sp.GetRequiredService<RequestContext>();

        await harness.Bus.Publish(new ConfirmStockV1(orderId, rc.TenantId));
        await harness.GetConsumerHarness<ConfirmStockConsumer>().Consumed.Any<ConfirmStockV1>();

        await using var verify = new InventoryDbContext(_tenant.Options);
        var ledger = await verify.Reservations.AsNoTracking()
            .Where(r => r.OrderId == orderId.ToString())
            .ToListAsync();
        ledger.Should().HaveCount(2);
        ledger.Should().OnlyContain(r => r.Status == ReservationStatus.Confirmed);

        var stockRows = await verify.StockItems.AsNoTracking().ToListAsync();
        stockRows.Single(s => s.Sku.Value == "SKU-A").Reserved.Value.Should().Be(0);
        stockRows.Single(s => s.Sku.Value == "SKU-B").Reserved.Value.Should().Be(0);

        var outbox = await verify
            .OutboxMessages.AsNoTracking()
            .CountAsync(o =>
                o.EventType.StartsWith("ShopFlow.Contracts.Inventory.StockConfirmedV1")
            );
        outbox.Should().Be(1);
    }

    [Fact]
    public async Task Consume_AlreadyConfirmed_StillEmitsStockConfirmedV1()
    {
        var orderId = Guid.NewGuid();
        await SeedMultiLineReservationAsync(orderId);

        await using var sp = await BuildHostAsync();
        var harness = sp.GetRequiredService<ITestHarness>();
        var rc = sp.GetRequiredService<RequestContext>();

        await harness.Bus.Publish(new ConfirmStockV1(orderId, rc.TenantId));
        await harness.GetConsumerHarness<ConfirmStockConsumer>().Consumed.Any<ConfirmStockV1>();

        await harness.Bus.Publish(new ConfirmStockV1(orderId, rc.TenantId));
        await Task.Delay(300);

        await using var verify = new InventoryDbContext(_tenant.Options);
        // Both deliveries emit StockConfirmedV1 (consumer treats
        // already-confirmed as success).
        var outbox = await verify
            .OutboxMessages.AsNoTracking()
            .CountAsync(o =>
                o.EventType.StartsWith("ShopFlow.Contracts.Inventory.StockConfirmedV1")
            );
        outbox.Should().Be(2);
    }
}
