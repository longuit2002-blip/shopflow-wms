using Microsoft.EntityFrameworkCore;
using ShopFlow.Inbound.Application.Services;
using ShopFlow.Inbound.Domain;
using ShopFlow.Inbound.Infrastructure;
using ShopFlow.Inbound.Infrastructure.Outbox;
using ShopFlow.Inbound.Infrastructure.Repositories;
using ShopFlow.SharedKernel.Application;

namespace ShopFlow.Inbound.IntegrationTests;

/// <summary>
/// Sprint-2-redux U3 — <see cref="ConfirmReceivingLineService"/> against
/// real Postgres. Validates per-line receiving flow end-to-end: PO state
/// transitions, reconciliation ticket creation on mismatch, idempotency
/// on (receiving_id, line_id), outbox emission for the
/// InboundLineConfirmedDomainEvent.
/// </summary>
[Collection(InboundTenantCollection.Name)]
[Trait("Category", "Integration")]
public sealed class ConfirmReceivingLineTests : IAsyncLifetime
{
    private static readonly DateTime Now = new(2026, 5, 13, 10, 0, 0, DateTimeKind.Utc);
    private static readonly TimeProvider FixedClock = new FakeClock(Now);

    private readonly InboundTenantFixture _fx;
    private ProvisionedInboundTenant _tenant = default!;

    public ConfirmReceivingLineTests(InboundTenantFixture fx)
    {
        _fx = fx;
    }

    public async Task InitializeAsync()
    {
        _tenant = await _fx.ProvisionTenantAsync("receive");
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private async Task<PurchaseOrder> SeedOpenPoAsync(params (string Sku, int Qty)[] lines)
    {
        await using var db = new InboundDbContext(_tenant.Options);
        var po = PurchaseOrder.Create("SUP-1", Now.AddDays(3), lines).Value!;
        po.Open(Now);
        await new PurchaseOrderRepository(db).AddAsync(po, CancellationToken.None);
        await db.SaveChangesAsync();
        return po;
    }

    private (ConfirmReceivingLineService Service, RequestContext Rc) BuildService(
        InboundDbContext db
    )
    {
        var rc = _tenant.BuildRequestContext();
        var service = new ConfirmReceivingLineService(
            poRepo: new PurchaseOrderRepository(db),
            receivingRepo: new ReceivingRepository(db),
            ticketRepo: new ReconciliationTicketRepository(db),
            outbox: new InboundOutbox(db, rc),
            requestContext: rc,
            uow: new InboundUnitOfWork(db),
            clock: FixedClock
        );
        return (service, rc);
    }

    [Fact]
    public async Task HappyPath_PartialReceipt_TransitionsToPartiallyReceived_OutboxRow()
    {
        var po = await SeedOpenPoAsync(("SKU-A", 100), ("SKU-B", 50));
        var lineA = po.Lines.First(l => l.Sku == "SKU-A");

        await using var db = new InboundDbContext(_tenant.Options);
        var (svc, _) = BuildService(db);

        var result = await svc.ConfirmAsync(
            purchaseOrderId: po.Id,
            receivingId: null,
            purchaseOrderLineId: lineA.Id,
            actualQty: 60,
            suggestedBinId: 7L,
            actualBinId: 7L,
            operatorId: null,
            ct: CancellationToken.None
        );

        result.IsSuccess.Should().BeTrue();
        result.Value!.TicketCreated.Should().BeTrue();
        result.Value.Idempotent.Should().BeFalse();

        await using var verifyDb = new InboundDbContext(_tenant.Options);
        var reloadedPo = await verifyDb
            .PurchaseOrders.Include(p => p.Lines)
            .FirstAsync(p => p.Id == po.Id);
        reloadedPo.Status.Should().Be(PurchaseOrderStatus.PartiallyReceived);
        reloadedPo.Lines.First(l => l.Sku == "SKU-A").ReceivedQty.Should().Be(60);

        var ticketCount = await verifyDb.ReconciliationTickets.CountAsync(t =>
            t.PurchaseOrderLineId == lineA.Id
        );
        ticketCount.Should().Be(1);

        var outboxCount = await verifyDb.OutboxMessages.CountAsync(o =>
            o.EventType.StartsWith("ShopFlow.Contracts.Inbound.InboundConfirmedV1")
        );
        outboxCount.Should().Be(1);
    }

    [Fact]
    public async Task ExactMatch_NoTicket()
    {
        var po = await SeedOpenPoAsync(("SKU-EXACT", 25));
        var line = po.Lines.Single();

        await using var db = new InboundDbContext(_tenant.Options);
        var (svc, _) = BuildService(db);

        var result = await svc.ConfirmAsync(
            purchaseOrderId: po.Id,
            receivingId: null,
            purchaseOrderLineId: line.Id,
            actualQty: 25,
            suggestedBinId: 1L,
            actualBinId: 1L,
            operatorId: null,
            ct: CancellationToken.None
        );

        result.IsSuccess.Should().BeTrue();
        result.Value!.TicketCreated.Should().BeFalse();

        await using var verifyDb = new InboundDbContext(_tenant.Options);
        var ticketCount = await verifyDb.ReconciliationTickets.CountAsync();
        ticketCount.Should().Be(0);

        var reloadedPo = await verifyDb
            .PurchaseOrders.Include(p => p.Lines)
            .FirstAsync(p => p.Id == po.Id);
        reloadedPo.Status.Should().Be(PurchaseOrderStatus.Closed);
    }

    [Fact]
    public async Task Overage_CreatesTicketAndAllowsClose()
    {
        var po = await SeedOpenPoAsync(("SKU-OVER", 100));
        var line = po.Lines.Single();

        await using var db = new InboundDbContext(_tenant.Options);
        var (svc, _) = BuildService(db);

        var result = await svc.ConfirmAsync(
            purchaseOrderId: po.Id,
            receivingId: null,
            purchaseOrderLineId: line.Id,
            actualQty: 110,
            suggestedBinId: 1L,
            actualBinId: 2L, // operator override
            operatorId: null,
            ct: CancellationToken.None
        );

        result.IsSuccess.Should().BeTrue();
        result.Value!.TicketCreated.Should().BeTrue();

        await using var verifyDb = new InboundDbContext(_tenant.Options);
        var ticket = await verifyDb.ReconciliationTickets.FirstAsync(t =>
            t.PurchaseOrderLineId == line.Id
        );
        ticket.ExpectedQty.Should().Be(100);
        ticket.ActualQty.Should().Be(110);
        ticket.Status.Should().Be(ReconciliationTicketStatus.Open);

        var reloadedPo = await verifyDb
            .PurchaseOrders.Include(p => p.Lines)
            .FirstAsync(p => p.Id == po.Id);
        reloadedPo.Status.Should().Be(PurchaseOrderStatus.Closed);
        reloadedPo.Lines.Single().ReceivedQty.Should().Be(110);

        var receivingLine = await verifyDb.ReceivingLines.FirstAsync(l =>
            l.PurchaseOrderLineId == line.Id
        );
        receivingLine.SuggestedBinId.Should().Be(1L);
        receivingLine.ActualBinId.Should().Be(2L);
    }

    [Fact]
    public async Task CancelledPo_FailsWithInvalidState()
    {
        var po = await SeedOpenPoAsync(("SKU-X", 5));
        await using (var setupDb = new InboundDbContext(_tenant.Options))
        {
            var reload = await setupDb
                .PurchaseOrders.Include(p => p.Lines)
                .FirstAsync(p => p.Id == po.Id);
            reload.Cancel("withdrawn", Now);
            await setupDb.SaveChangesAsync();
        }

        await using var db = new InboundDbContext(_tenant.Options);
        var (svc, _) = BuildService(db);

        var result = await svc.ConfirmAsync(
            purchaseOrderId: po.Id,
            receivingId: null,
            purchaseOrderLineId: po.Lines.Single().Id,
            actualQty: 5,
            suggestedBinId: 1L,
            actualBinId: 1L,
            operatorId: null,
            ct: CancellationToken.None
        );

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be("po.invalid_state");
    }

    [Fact]
    public async Task UnknownPo_FailsWithNotFound()
    {
        await using var db = new InboundDbContext(_tenant.Options);
        var (svc, _) = BuildService(db);

        var result = await svc.ConfirmAsync(
            purchaseOrderId: Guid.NewGuid(),
            receivingId: null,
            purchaseOrderLineId: Guid.NewGuid(),
            actualQty: 1,
            suggestedBinId: 1L,
            actualBinId: 1L,
            operatorId: null,
            ct: CancellationToken.None
        );

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be("receiving.po_not_found");
    }

    [Fact]
    public async Task IdempotentRetry_SameReceivingAndLine_DoesNotWriteSecondRow()
    {
        var po = await SeedOpenPoAsync(("SKU-IDEMP", 50));
        var line = po.Lines.Single();

        await using var db = new InboundDbContext(_tenant.Options);
        var (svc, _) = BuildService(db);

        var first = await svc.ConfirmAsync(
            po.Id,
            null,
            line.Id,
            20,
            1L,
            1L,
            null,
            CancellationToken.None
        );
        first.IsSuccess.Should().BeTrue();
        var receivingId = first.Value!.ReceivingId;

        await using var db2 = new InboundDbContext(_tenant.Options);
        var (svc2, _) = BuildService(db2);
        var second = await svc2.ConfirmAsync(
            po.Id,
            receivingId,
            line.Id,
            20,
            1L,
            1L,
            null,
            CancellationToken.None
        );

        second.IsSuccess.Should().BeTrue();
        second.Value!.Idempotent.Should().BeTrue();
        second.Value.TicketCreated.Should().BeFalse();
        second.Value.ReceivingLineId.Should().Be(first.Value.ReceivingLineId);

        await using var verifyDb = new InboundDbContext(_tenant.Options);
        var lineCount = await verifyDb.ReceivingLines.CountAsync(l =>
            l.PurchaseOrderLineId == line.Id
        );
        lineCount.Should().Be(1);
        var reloaded = await verifyDb
            .PurchaseOrders.Include(p => p.Lines)
            .FirstAsync(p => p.Id == po.Id);
        // Receipt was only applied once.
        reloaded.Lines.Single().ReceivedQty.Should().Be(20);
    }

    private sealed class FakeClock : TimeProvider
    {
        private readonly DateTimeOffset _now;

        public FakeClock(DateTime utcNow)
        {
            _now = new DateTimeOffset(utcNow, TimeSpan.Zero);
        }

        public override DateTimeOffset GetUtcNow() => _now;
    }
}
