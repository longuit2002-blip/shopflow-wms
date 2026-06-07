using Microsoft.EntityFrameworkCore;
using ShopFlow.Inbound.Domain;
using ShopFlow.Inbound.Infrastructure;
using ShopFlow.Inbound.Infrastructure.Repositories;

namespace ShopFlow.Inbound.IntegrationTests;

/// <summary>
/// Sprint-2-redux U2 — <see cref="PurchaseOrderRepository"/> against real
/// Postgres. Validates the EF round-trip preserves the aggregate state
/// (Status, ReceivedQty, lines) across save + reload.
/// </summary>
[Collection(InboundTenantCollection.Name)]
[Trait("Category", "Integration")]
public sealed class PurchaseOrderRepositoryTests : IAsyncLifetime
{
    private static readonly DateTime Now = new(2026, 5, 13, 10, 0, 0, DateTimeKind.Utc);

    private readonly InboundTenantFixture _fx;
    private ProvisionedInboundTenant _tenant = default!;

    public PurchaseOrderRepositoryTests(InboundTenantFixture fx)
    {
        _fx = fx;
    }

    public async Task InitializeAsync()
    {
        _tenant = await _fx.ProvisionTenantAsync("po-repo");
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task AddAsync_ThenFindById_RoundTripsPoWithLines()
    {
        await using var dbWrite = new InboundDbContext(_tenant.Options);
        var po = PurchaseOrder
            .Create("SUP-1", Now.AddDays(3), new[] { ("SKU-A", 100), ("SKU-B", 50) })
            .Value!;
        var repoWrite = new PurchaseOrderRepository(dbWrite);
        await repoWrite.AddAsync(po, CancellationToken.None);
        await dbWrite.SaveChangesAsync();

        await using var dbRead = new InboundDbContext(_tenant.Options);
        var repoRead = new PurchaseOrderRepository(dbRead);
        var found = await repoRead.FindByIdAsync(po.Id, CancellationToken.None);

        found.Should().NotBeNull();
        found!.SupplierRef.Should().Be("SUP-1");
        found.Status.Should().Be(PurchaseOrderStatus.Draft);
        found.Lines.Should().HaveCount(2);
        found.Lines.Sum(l => l.ExpectedQty).Should().Be(150);
    }

    [Fact]
    public async Task FindByIdAsync_Unknown_ReturnsNull()
    {
        await using var db = new InboundDbContext(_tenant.Options);
        var repo = new PurchaseOrderRepository(db);

        var found = await repo.FindByIdAsync(Guid.NewGuid(), CancellationToken.None);

        found.Should().BeNull();
    }

    [Fact]
    public async Task StateMachineRoundTrip_OpenAndReceive_PersistsStateAndReceivedQty()
    {
        await using var dbWrite = new InboundDbContext(_tenant.Options);
        var po = PurchaseOrder.Create("SUP-2", Now.AddDays(3), new[] { ("SKU-X", 10) }).Value!;
        po.Open(Now).IsSuccess.Should().BeTrue();
        po.RecordLineReceipt(po.Lines.Single().Id, 10, Now.AddMinutes(5))
            .IsSuccess.Should()
            .BeTrue();
        await new PurchaseOrderRepository(dbWrite).AddAsync(po, CancellationToken.None);
        await dbWrite.SaveChangesAsync();

        await using var dbRead = new InboundDbContext(_tenant.Options);
        var reloaded = await new PurchaseOrderRepository(dbRead).FindByIdAsync(
            po.Id,
            CancellationToken.None
        );

        reloaded.Should().NotBeNull();
        reloaded!.Status.Should().Be(PurchaseOrderStatus.Closed);
        reloaded.OpenedAt.Should().Be(Now);
        reloaded.ClosedAt.Should().NotBeNull();
        reloaded.Lines.Single().ReceivedQty.Should().Be(10);
    }

    [Fact]
    public async Task ListOpenAsync_ReturnsOnlyOpenAndPartiallyReceived_OrderedByEta()
    {
        await using var db = new InboundDbContext(_tenant.Options);
        var repo = new PurchaseOrderRepository(db);

        var draft = PurchaseOrder.Create("SUP-D", Now.AddDays(1), new[] { ("SKU-1", 5) }).Value!;
        var openLate = PurchaseOrder
            .Create("SUP-OPEN-LATE", Now.AddDays(5), new[] { ("SKU-2", 5) })
            .Value!;
        openLate.Open(Now);
        var openEarly = PurchaseOrder
            .Create("SUP-OPEN-EARLY", Now.AddDays(2), new[] { ("SKU-3", 5) })
            .Value!;
        openEarly.Open(Now);
        var cancelled = PurchaseOrder
            .Create("SUP-X", Now.AddDays(3), new[] { ("SKU-4", 5) })
            .Value!;
        cancelled.Cancel("nope", Now);

        await repo.AddAsync(draft, CancellationToken.None);
        await repo.AddAsync(openLate, CancellationToken.None);
        await repo.AddAsync(openEarly, CancellationToken.None);
        await repo.AddAsync(cancelled, CancellationToken.None);
        await db.SaveChangesAsync();

        var openList = await repo.ListOpenAsync(CancellationToken.None);
        openList
            .Select(p => p.SupplierRef)
            .Should()
            .ContainInOrder(new[] { "SUP-OPEN-EARLY", "SUP-OPEN-LATE" });
        openList.Should().HaveCount(2);
    }
}
