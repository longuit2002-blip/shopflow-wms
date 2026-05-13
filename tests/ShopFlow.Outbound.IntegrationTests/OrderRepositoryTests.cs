using ShopFlow.Outbound.Domain;
using ShopFlow.Outbound.Infrastructure;
using ShopFlow.Outbound.Infrastructure.Repositories;

namespace ShopFlow.Outbound.IntegrationTests;

/// <summary>
/// Sprint-3-redux U2 — <see cref="OrderRepository"/> against real Postgres.
/// Validates the EF round-trip preserves the aggregate state (Status,
/// ExpectedWeightTotal, lines) across save + reload, and that the
/// <see cref="OrderRepository.FindByExternalIdAsync"/> idempotency anchor
/// returns the existing order on a duplicate channel ref.
/// </summary>
[Collection(OutboundTenantCollection.Name)]
[Trait("Category", "Integration")]
public sealed class OrderRepositoryTests : IAsyncLifetime
{
    private readonly OutboundTenantFixture _fx;
    private ProvisionedOutboundTenant _tenant = default!;

    public OrderRepositoryTests(OutboundTenantFixture fx)
    {
        _fx = fx;
    }

    public async Task InitializeAsync()
    {
        _tenant = await _fx.ProvisionTenantAsync("order-repo");
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task AddAsync_ThenFindById_RoundTripsOrderWithLines()
    {
        await using var dbWrite = new OutboundDbContext(_tenant.Options);
        var order = Order
            .Create(
                "ext-roundtrip",
                "standard",
                new[] { ("SKU-A", 2, (int?)100), ("SKU-B", 5, (int?)50) }
            )
            .Value!;
        var repoWrite = new OrderRepository(dbWrite);
        await repoWrite.AddAsync(order, CancellationToken.None);
        await dbWrite.SaveChangesAsync();

        await using var dbRead = new OutboundDbContext(_tenant.Options);
        var repoRead = new OrderRepository(dbRead);
        var found = await repoRead.FindByIdAsync(order.Id, CancellationToken.None);

        found.Should().NotBeNull();
        found!.ChannelExternalOrderId.Should().Be("ext-roundtrip");
        found.ShippingProfile.Should().Be("standard");
        found.Status.Should().Be(OrderStatus.Created);
        found.ExpectedWeightTotal.Should().Be(450);
        found.Lines.Should().HaveCount(2);
        found.Lines.Single(l => l.Sku == "SKU-A").Qty.Should().Be(2);
        found.Lines.Single(l => l.Sku == "SKU-B").ExpectedWeight.Should().Be(50);
    }

    [Fact]
    public async Task FindByIdAsync_Unknown_ReturnsNull()
    {
        await using var db = new OutboundDbContext(_tenant.Options);
        var repo = new OrderRepository(db);

        var found = await repo.FindByIdAsync(Guid.NewGuid(), CancellationToken.None);

        found.Should().BeNull();
    }

    [Fact]
    public async Task FindByExternalIdAsync_KnownExternalId_ReturnsOrderWithLines()
    {
        await using var dbWrite = new OutboundDbContext(_tenant.Options);
        var order = Order
            .Create("ext-find-by-external", "express", new[] { ("SKU-X", 3, (int?)null) })
            .Value!;
        await new OrderRepository(dbWrite).AddAsync(order, CancellationToken.None);
        await dbWrite.SaveChangesAsync();

        await using var dbRead = new OutboundDbContext(_tenant.Options);
        var found = await new OrderRepository(dbRead)
            .FindByExternalIdAsync("ext-find-by-external", CancellationToken.None);

        found.Should().NotBeNull();
        found!.Id.Should().Be(order.Id);
        found.Lines.Should().HaveCount(1);
        found.ExpectedWeightTotal.Should().BeNull();
    }

    [Fact]
    public async Task FindByExternalIdAsync_UnknownExternalId_ReturnsNull()
    {
        await using var db = new OutboundDbContext(_tenant.Options);
        var repo = new OrderRepository(db);

        var found = await repo.FindByExternalIdAsync("never-existed", CancellationToken.None);

        found.Should().BeNull();
    }

    [Fact]
    public async Task StateMachineRoundTrip_TransitionsAndShipmentMetadata_PersistAcrossReload()
    {
        await using var dbWrite = new OutboundDbContext(_tenant.Options);
        var order = Order
            .Create("ext-state", "standard", new[] { ("SKU-S", 1, (int?)200) })
            .Value!;
        order.MarkAwaitingReservation();
        order.MarkReserved();
        order.MarkAwaitingPick();
        order.MarkPicked();
        order.MarkPacked(actualWeightTotal: 220);
        order.MarkAwaitingShip();
        order.MarkShipped("https://carrier/abc", "TRK-RR-1");
        await new OrderRepository(dbWrite).AddAsync(order, CancellationToken.None);
        await dbWrite.SaveChangesAsync();

        await using var dbRead = new OutboundDbContext(_tenant.Options);
        var reloaded = await new OrderRepository(dbRead)
            .FindByIdAsync(order.Id, CancellationToken.None);

        reloaded.Should().NotBeNull();
        reloaded!.Status.Should().Be(OrderStatus.Shipped);
        reloaded.ActualWeightTotal.Should().Be(220);
        reloaded.LabelUrl.Should().Be("https://carrier/abc");
        reloaded.TrackingNumber.Should().Be("TRK-RR-1");
    }
}
