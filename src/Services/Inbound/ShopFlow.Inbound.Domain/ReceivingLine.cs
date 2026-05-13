using ShopFlow.SharedKernel.Domain;

namespace ShopFlow.Inbound.Domain;

/// <summary>
/// One line confirmation inside a <see cref="Receiving"/>. Captures the
/// actual quantity received, the bin the system suggested, and the bin the
/// operator chose (may differ if operator overrode the suggestion per
/// Sprint-2-redux plan R7). Idempotency anchor: composite
/// <c>UNIQUE(receiving_id, purchase_order_line_id)</c>.
/// </summary>
public sealed class ReceivingLine : BaseEntity
{
    public Guid ReceivingId { get; private set; }

    public Guid PurchaseOrderLineId { get; private set; }

    public int ActualQty { get; private set; }

    public long SuggestedBinId { get; private set; }

    public long ActualBinId { get; private set; }

    private ReceivingLine() { }

    internal static Result<ReceivingLine> Create(
        Guid receivingId,
        Guid purchaseOrderLineId,
        int actualQty,
        long suggestedBinId,
        long actualBinId
    )
    {
        if (actualQty < 0)
        {
            return Result<ReceivingLine>.Failure(
                "actual_qty must be >= 0.",
                "receiving_line.actual_qty_negative"
            );
        }
        return Result<ReceivingLine>.Success(
            new ReceivingLine
            {
                ReceivingId = receivingId,
                PurchaseOrderLineId = purchaseOrderLineId,
                ActualQty = actualQty,
                SuggestedBinId = suggestedBinId,
                ActualBinId = actualBinId,
            }
        );
    }
}
