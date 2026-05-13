using ShopFlow.SharedKernel.Domain;

namespace ShopFlow.Inbound.Domain;

/// <summary>
/// One line item inside a <see cref="PurchaseOrder"/>: expected SKU + quantity
/// plus a running <c>ReceivedQty</c> that the Receiving flow (U3) updates.
/// </summary>
/// <remarks>
/// Part of the <see cref="PurchaseOrder"/> aggregate — no independent
/// repository, no independent lifecycle. <c>RecordReceipt</c> stays internal:
/// invoked only by <c>PurchaseOrder.RecordLineReceipt</c> so the parent
/// recomputes its own status atomically.
/// </remarks>
public sealed class PurchaseOrderLine : BaseEntity
{
    public Guid PurchaseOrderId { get; private set; }

    public string Sku { get; private set; } = string.Empty;

    public int ExpectedQty { get; private set; }

    public int ReceivedQty { get; private set; }

    private PurchaseOrderLine() { }

    internal static Result<PurchaseOrderLine> Create(
        Guid purchaseOrderId,
        string sku,
        int expectedQty
    )
    {
        if (string.IsNullOrWhiteSpace(sku))
        {
            return Result<PurchaseOrderLine>.Failure(
                "sku is required.",
                "po_line.sku_required"
            );
        }
        if (expectedQty <= 0)
        {
            return Result<PurchaseOrderLine>.Failure(
                "expected_qty must be > 0.",
                "po_line.expected_qty_non_positive"
            );
        }

        return Result<PurchaseOrderLine>.Success(
            new PurchaseOrderLine
            {
                PurchaseOrderId = purchaseOrderId,
                Sku = sku.Trim(),
                ExpectedQty = expectedQty,
                ReceivedQty = 0,
            }
        );
    }

    /// <summary>
    /// Apply receipt. Invoked only via <c>PurchaseOrder.RecordLineReceipt</c>.
    /// Sprint-2-redux U3 body.
    /// </summary>
    internal Result RecordReceipt(int actualQty, DateTime now)
    {
        _ = (actualQty, now);
        throw new NotImplementedException(
            "Sprint-2-redux U3 behavior — see docs/plans/2026-05-13-001-feat-phase-1-sprint-2-redux-inbound-plan.md"
        );
    }
}
