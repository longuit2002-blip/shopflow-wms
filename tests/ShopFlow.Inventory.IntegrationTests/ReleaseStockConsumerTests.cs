using System.Text.Json;
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
/// Sprint-3-redux U3 — <see cref="ReleaseStockConsumer"/> tests. Covers
/// full-release (empty OrderLineIds), partial-set release, and
/// already-released idempotency.
/// </summary>
[Collection(InventoryTenantCollection.Name)]
[Trait("Category", "Integration")]
public sealed class ReleaseStockConsumerTests : IAsyncLifetime
{
    private readonly InventoryTenantFixture _fx;
    private ProvisionedTenant _tenant = default!;

    public ReleaseStockConsumerTests(InventoryTenantFixture fx)
    {
        _fx = fx;
    }

    public async Task InitializeAsync()
    {
        _tenant = await _fx.ProvisionTenantAsync("release-cons");
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

        services.AddMassTransitTestHarness(cfg => cfg.AddConsumer<ReleaseStockConsumer>());

        var sp = services.BuildServiceProvider(true);
        await sp.GetRequiredService<ITestHarness>().Start();
        return sp;
    }

    private async Task SeedMultiLineReservationAsync(Guid orderId)
    {
        await using var db = new InventoryDbContext(_tenant.Options);
        var repo = new ReservationRepository(
            db,
            TimeProvider.System,
            _tenant.BuildRequestContext()
        );
        var lines = new[]
        {
            new LineReservation(Sku.Create("SKU-A"), "L1", Quantity.From(7)),
            new LineReservation(Sku.Create("SKU-B"), "L2", Quantity.From(3)),
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
    public async Task Consume_FullRelease_EmptyOrderLineIds_ReleasesAllRows()
    {
        var orderId = Guid.NewGuid();
        await SeedMultiLineReservationAsync(orderId);

        await using var sp = await BuildHostAsync();
        var harness = sp.GetRequiredService<ITestHarness>();
        var rc = sp.GetRequiredService<RequestContext>();

        await harness.Bus.Publish(new ReleaseStockV1(orderId, rc.TenantId, Array.Empty<string>()));
        await harness.GetConsumerHarness<ReleaseStockConsumer>().Consumed.Any<ReleaseStockV1>();

        await using var verify = new InventoryDbContext(_tenant.Options);
        var ledger = await verify
            .Reservations.AsNoTracking()
            .Where(r => r.OrderId == orderId.ToString())
            .ToListAsync();
        ledger.Should().OnlyContain(r => r.Status == ReservationStatus.Released);

        // Stock restored to original.
        var stocks = await verify.StockItems.AsNoTracking().ToListAsync();
        stocks.Single(s => s.Sku.Value == "SKU-A").Available.Value.Should().Be(50);
        stocks.Single(s => s.Sku.Value == "SKU-B").Available.Value.Should().Be(30);
    }

    [Fact]
    public async Task Consume_PartialSet_OnlyReleasesListedLines()
    {
        var orderId = Guid.NewGuid();
        await SeedMultiLineReservationAsync(orderId);

        await using var sp = await BuildHostAsync();
        var harness = sp.GetRequiredService<ITestHarness>();
        var rc = sp.GetRequiredService<RequestContext>();

        await harness.Bus.Publish(new ReleaseStockV1(orderId, rc.TenantId, new[] { "L1" }));
        await harness.GetConsumerHarness<ReleaseStockConsumer>().Consumed.Any<ReleaseStockV1>();

        await using var verify = new InventoryDbContext(_tenant.Options);
        var ledger = await verify
            .Reservations.AsNoTracking()
            .Where(r => r.OrderId == orderId.ToString())
            .ToListAsync();
        ledger.Single(r => r.OrderLineId == "L1").Status.Should().Be(ReservationStatus.Released);
        ledger.Single(r => r.OrderLineId == "L2").Status.Should().Be(ReservationStatus.Pending);

        // Emitted StockReleasedV1 carries actually-released line ids.
        var outboxRows = await verify
            .OutboxMessages.AsNoTracking()
            .Where(o => o.EventType.StartsWith("ShopFlow.Contracts.Inventory.StockReleasedV1"))
            .ToListAsync();
        outboxRows.Should().HaveCount(1);
        using var doc = JsonDocument.Parse(outboxRows.Single().Payload);
        var ids = doc
            .RootElement.GetProperty("orderLineIds")
            .EnumerateArray()
            .Select(e => e.GetString())
            .ToArray();
        ids.Should().BeEquivalentTo(new[] { "L1" });
    }

    [Fact]
    public async Task Consume_AlreadyReleased_EmitsEmptyOrderLineIdsList()
    {
        var orderId = Guid.NewGuid();
        await SeedMultiLineReservationAsync(orderId);

        await using var sp = await BuildHostAsync();
        var harness = sp.GetRequiredService<ITestHarness>();
        var rc = sp.GetRequiredService<RequestContext>();

        // First partial release
        await harness.Bus.Publish(new ReleaseStockV1(orderId, rc.TenantId, new[] { "L1" }));
        await harness.GetConsumerHarness<ReleaseStockConsumer>().Consumed.Any<ReleaseStockV1>();

        // Redelivery of same release — nothing matches Pending, returns empty.
        await harness.Bus.Publish(new ReleaseStockV1(orderId, rc.TenantId, new[] { "L1" }));
        await Task.Delay(300);

        await using var verify = new InventoryDbContext(_tenant.Options);
        var outboxRows = await verify
            .OutboxMessages.AsNoTracking()
            .Where(o => o.EventType.StartsWith("ShopFlow.Contracts.Inventory.StockReleasedV1"))
            .OrderBy(o => o.CreatedAt)
            .ToListAsync();
        outboxRows.Should().HaveCount(2);

        using var first = JsonDocument.Parse(outboxRows[0].Payload);
        first
            .RootElement.GetProperty("orderLineIds")
            .EnumerateArray()
            .Select(e => e.GetString())
            .Should()
            .BeEquivalentTo(new[] { "L1" });

        using var second = JsonDocument.Parse(outboxRows[1].Payload);
        second.RootElement.GetProperty("orderLineIds").GetArrayLength().Should().Be(0);
    }
}
