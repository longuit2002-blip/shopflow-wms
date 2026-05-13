using ShopFlow.Contracts.Inbound;
using ShopFlow.Inbound.Application.Ports;
using ShopFlow.Inbound.Domain;
using ShopFlow.SharedKernel.Application;
using ShopFlow.SharedKernel.Domain;

namespace ShopFlow.Inbound.Application.Services;

/// <summary>
/// Orchestrates the per-line receiving flow per Sprint-2-redux plan R4-R10.
/// Coordinates three aggregates inside one tenant transaction:
/// </summary>
/// <list type="number">
///   <item><description>Load (or create) the <see cref="Receiving"/> session against the target PO.</description></item>
///   <item><description>Resolve the <see cref="PurchaseOrderLine"/> on the PO; capture its <c>ExpectedQty</c> + <c>Sku</c>.</description></item>
///   <item><description>Idempotency check on <c>(receiving_id, purchase_order_line_id)</c>: if already confirmed return the existing line as success (R6).</description></item>
///   <item><description>Add the <see cref="ReceivingLine"/> via <see cref="Receiving.AddConfirmedLine"/> — raises <c>InboundLineConfirmedDomainEvent</c> for the OutboxInterceptor to harvest.</description></item>
///   <item><description>Invoke <see cref="PurchaseOrder.RecordLineReceipt"/> to roll the running <c>ReceivedQty</c> + auto-transition the PO state per R8.</description></item>
///   <item><description>If <paramref name="actualQty"/> != line's expected qty, write a <see cref="ReconciliationTicket"/> row per R9.</description></item>
///   <item><description><see cref="IUnitOfWork.SaveChangesAsync"/> flushes everything atomically; outbox row lands in the same transaction.</description></item>
/// </list>
/// <remarks>
/// MediatR command + handler wrapper lands in U8 alongside HTTP endpoints.
/// Sprint-2-redux U3 ships the service so integration tests can exercise
/// the flow end-to-end (minus HTTP) and U6 can wire the consumer side.
/// </remarks>
public sealed class ConfirmReceivingLineService
{
    private readonly IPurchaseOrderRepository _poRepo;
    private readonly IReceivingRepository _receivingRepo;
    private readonly IReconciliationTicketRepository _ticketRepo;
    private readonly IInboundOutbox _outbox;
    private readonly IRequestContext _requestContext;
    private readonly IUnitOfWork _uow;
    private readonly TimeProvider _clock;

    public ConfirmReceivingLineService(
        IPurchaseOrderRepository poRepo,
        IReceivingRepository receivingRepo,
        IReconciliationTicketRepository ticketRepo,
        IInboundOutbox outbox,
        IRequestContext requestContext,
        IUnitOfWork uow,
        TimeProvider clock
    )
    {
        _poRepo = poRepo;
        _receivingRepo = receivingRepo;
        _ticketRepo = ticketRepo;
        _outbox = outbox;
        _requestContext = requestContext;
        _uow = uow;
        _clock = clock;
    }

    public async Task<Result<ConfirmReceivingLineOutcome>> ConfirmAsync(
        Guid purchaseOrderId,
        Guid? receivingId,
        Guid purchaseOrderLineId,
        int actualQty,
        long suggestedBinId,
        long actualBinId,
        Guid? operatorId,
        CancellationToken ct
    )
    {
        if (actualQty < 0)
        {
            return Result<ConfirmReceivingLineOutcome>.Failure(
                "actual_qty must be >= 0.",
                "receiving.actual_qty_negative"
            );
        }

        var po = await _poRepo.FindByIdAsync(purchaseOrderId, ct).ConfigureAwait(false);
        if (po is null)
        {
            return Result<ConfirmReceivingLineOutcome>.Failure(
                $"purchase order {purchaseOrderId} not found.",
                "receiving.po_not_found"
            );
        }

        var poLine = po.Lines.FirstOrDefault(l => l.Id == purchaseOrderLineId);
        if (poLine is null)
        {
            return Result<ConfirmReceivingLineOutcome>.Failure(
                $"line {purchaseOrderLineId} not found on PO {purchaseOrderId}.",
                "receiving.line_not_found"
            );
        }

        var nowUtc = _clock.GetUtcNow().UtcDateTime;

        Receiving? receiving = null;
        if (receivingId is not null)
        {
            receiving = await _receivingRepo
                .FindByIdAsync(receivingId.Value, ct)
                .ConfigureAwait(false);
            if (receiving is null)
            {
                return Result<ConfirmReceivingLineOutcome>.Failure(
                    $"receiving {receivingId} not found.",
                    "receiving.session_not_found"
                );
            }
            if (receiving.PurchaseOrderId != purchaseOrderId)
            {
                return Result<ConfirmReceivingLineOutcome>.Failure(
                    "receiving session is bound to a different PO.",
                    "receiving.po_mismatch"
                );
            }
            // Idempotency: if the same (receiving_id, po_line_id) was already
            // confirmed, return the existing row as success without writing
            // anything (R6).
            var existingLine = receiving.Lines.FirstOrDefault(l =>
                l.PurchaseOrderLineId == purchaseOrderLineId
            );
            if (existingLine is not null)
            {
                return Result<ConfirmReceivingLineOutcome>.Success(
                    new ConfirmReceivingLineOutcome(
                        ReceivingId: receiving.Id,
                        ReceivingLineId: existingLine.Id,
                        Idempotent: true,
                        TicketCreated: false
                    )
                );
            }
        }
        else
        {
            var receivingResult = Receiving.Create(purchaseOrderId, operatorId, nowUtc);
            if (!receivingResult.IsSuccess)
            {
                return Result<ConfirmReceivingLineOutcome>.Failure(
                    receivingResult.Error!,
                    receivingResult.ErrorCode
                );
            }
            receiving = receivingResult.Value!;
            await _receivingRepo.AddAsync(receiving, ct).ConfigureAwait(false);
        }

        var lineResult = receiving.AddConfirmedLine(
            purchaseOrderLineId,
            actualQty,
            suggestedBinId,
            actualBinId,
            poLine.Sku
        );
        if (!lineResult.IsSuccess)
        {
            return Result<ConfirmReceivingLineOutcome>.Failure(
                lineResult.Error!,
                lineResult.ErrorCode
            );
        }

        var poReceipt = po.RecordLineReceipt(purchaseOrderLineId, actualQty, nowUtc);
        if (!poReceipt.IsSuccess)
        {
            return Result<ConfirmReceivingLineOutcome>.Failure(
                poReceipt.Error!,
                poReceipt.ErrorCode
            );
        }

        var ticketCreated = false;
        if (actualQty != poLine.ExpectedQty)
        {
            var ticketResult = ReconciliationTicket.Open(
                purchaseOrderId: purchaseOrderId,
                purchaseOrderLineId: purchaseOrderLineId,
                receivingId: receiving.Id,
                sku: poLine.Sku,
                expectedQty: poLine.ExpectedQty,
                actualQty: actualQty,
                occurredAt: nowUtc
            );
            if (!ticketResult.IsSuccess)
            {
                return Result<ConfirmReceivingLineOutcome>.Failure(
                    ticketResult.Error!,
                    ticketResult.ErrorCode
                );
            }
            await _ticketRepo.AddAsync(ticketResult.Value!, ct).ConfigureAwait(false);
            ticketCreated = true;
        }

        // Write the cross-module integration event explicitly into the
        // outbox. Lives in the same SaveChanges transaction as the
        // business writes above; the multiplexed dispatcher publishes
        // through MassTransit once the row commits.
        _outbox.Enqueue(
            new InboundConfirmedV1(
                PurchaseOrderId: purchaseOrderId,
                PurchaseOrderLineId: purchaseOrderLineId,
                ReceivingId: receiving.Id,
                Sku: poLine.Sku,
                ActualQuantity: actualQty,
                BinId: actualBinId,
                TenantId: _requestContext.TenantId,
                OccurredAt: nowUtc
            ),
            nowUtc
        );

        await _uow.SaveChangesAsync(ct).ConfigureAwait(false);

        return Result<ConfirmReceivingLineOutcome>.Success(
            new ConfirmReceivingLineOutcome(
                ReceivingId: receiving.Id,
                ReceivingLineId: lineResult.Value!.Id,
                Idempotent: false,
                TicketCreated: ticketCreated
            )
        );
    }
}

public sealed record ConfirmReceivingLineOutcome(
    Guid ReceivingId,
    Guid ReceivingLineId,
    bool Idempotent,
    bool TicketCreated
);
