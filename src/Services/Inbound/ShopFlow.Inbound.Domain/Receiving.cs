using ShopFlow.Inbound.Domain.Events;
using ShopFlow.SharedKernel.Domain;

namespace ShopFlow.Inbound.Domain;

/// <summary>
/// Aggregate root for one receiving session against a <see cref="PurchaseOrder"/>.
/// Per Sprint-2-redux plan R4 receiving is per-line and a PO can have many
/// receiving sessions (supports partial delivery). Each line confirmation
/// captures actual qty, suggested bin id, actual bin id, operator id, and
/// occurrence timestamp per plan R5.
/// </summary>
/// <remarks>
/// U1 ships the type shape; U3 fills in <c>AddConfirmedLine</c> (the state
/// machine + reconciliation-ticket trigger + outbox event raise) — currently
/// throws <see cref="NotImplementedException"/>. Idempotency anchor is
/// <c>UNIQUE(receiving_id, line_id)</c> on <c>receiving_lines</c>; duplicate
/// confirmations surface as a no-op success at the repository layer.
/// </remarks>
public sealed class Receiving : BaseEntity
{
    public Guid PurchaseOrderId { get; private set; }

    public Guid? OperatorId { get; private set; }

    public DateTime OccurredAt { get; private set; }

    private readonly List<ReceivingLine> _lines = new();

    public IReadOnlyList<ReceivingLine> Lines => _lines.AsReadOnly();

    private Receiving() { }

    /// <summary>
    /// Open a receiving session. Sprint-2-redux U3 will tighten the
    /// validation (e.g., reject if PO is in Cancelled state).
    /// </summary>
    public static Result<Receiving> Create(
        Guid purchaseOrderId,
        Guid? operatorId,
        DateTime occurredAt
    )
    {
        if (purchaseOrderId == Guid.Empty)
        {
            return Result<Receiving>.Failure(
                "purchase_order_id is required.",
                "receiving.po_id_required"
            );
        }
        return Result<Receiving>.Success(
            new Receiving
            {
                PurchaseOrderId = purchaseOrderId,
                OperatorId = operatorId,
                OccurredAt = occurredAt,
            }
        );
    }

    /// <summary>
    /// Confirm one line in the session. Records the receiving-line row,
    /// raises <see cref="InboundLineConfirmedDomainEvent"/> for the
    /// OutboxInterceptor to harvest. The caller is responsible for
    /// invoking <c>PurchaseOrder.RecordLineReceipt</c> separately (handler
    /// orchestrates both aggregates in the same transaction) and for
    /// creating a <see cref="ReconciliationTicket"/> if
    /// <paramref name="actualQty"/> differs from the expected qty on the PO
    /// line. <paramref name="sku"/> is carried in so the event payload is
    /// self-contained without a navigation chase on the consumer side.
    /// </summary>
    public Result<ReceivingLine> AddConfirmedLine(
        Guid purchaseOrderLineId,
        int actualQty,
        long suggestedBinId,
        long actualBinId,
        string sku
    )
    {
        if (string.IsNullOrWhiteSpace(sku))
        {
            return Result<ReceivingLine>.Failure(
                "sku is required.",
                "receiving.sku_required"
            );
        }
        if (_lines.Any(l => l.PurchaseOrderLineId == purchaseOrderLineId))
        {
            return Result<ReceivingLine>.Failure(
                $"line {purchaseOrderLineId} already confirmed in this receiving.",
                "receiving.line_already_confirmed"
            );
        }

        var lineResult = ReceivingLine.Create(
            Id,
            purchaseOrderLineId,
            actualQty,
            suggestedBinId,
            actualBinId
        );
        if (!lineResult.IsSuccess)
        {
            return lineResult;
        }

        _lines.Add(lineResult.Value!);
        UpdatedAt = DateTime.UtcNow;

        RaiseDomainEvent(
            new InboundLineConfirmedDomainEvent(
                PurchaseOrderId: PurchaseOrderId,
                PurchaseOrderLineId: purchaseOrderLineId,
                ReceivingId: Id,
                Sku: sku.Trim(),
                ActualQuantity: actualQty,
                BinId: actualBinId,
                OccurredAt: OccurredAt
            )
        );

        return lineResult;
    }
}
