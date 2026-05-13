using ShopFlow.SharedKernel.Domain;

namespace ShopFlow.Inbound.Domain;

/// <summary>
/// Aggregate root for one purchase order received from a supplier per Tech
/// Design v3.0 §11.3 + Sprint-2-redux plan R1-R3. Carries the lifecycle
/// state machine (<see cref="PurchaseOrderStatus"/>), supplier reference,
/// expected delivery, and N line items each tracking expected vs
/// running-received quantity.
/// </summary>
/// <remarks>
/// <para>U1 ships the type shape + a <c>Create</c> factory; U2 fills in the
/// state-machine methods (<c>Open</c>, <c>MarkPartiallyReceived</c>,
/// <c>Close</c>, <c>Cancel</c>) — they currently throw
/// <see cref="NotImplementedException"/>. <see cref="PurchaseOrderLine.RecordReceipt"/>
/// stays internal to the aggregate; Receiving (U3) invokes it from outside.</para>
///
/// <para>Per ADR-0003 no <c>tenant_id</c> column — the database identity is
/// the tenant boundary. Outbox-style events raised here flow through
/// <c>OutboxInterceptor</c> with the tenant id stamped from
/// <c>IRequestContext</c> at write time.</para>
/// </remarks>
public sealed class PurchaseOrder : BaseEntity
{
    public string SupplierRef { get; private set; } = string.Empty;

    public DateTime ExpectedDeliveryAt { get; private set; }

    public PurchaseOrderStatus Status { get; private set; } = PurchaseOrderStatus.Draft;

    public DateTime? OpenedAt { get; private set; }

    public DateTime? ClosedAt { get; private set; }

    public DateTime? CancelledAt { get; private set; }

    public string? CancellationReason { get; private set; }

    private readonly List<PurchaseOrderLine> _lines = new();

    public IReadOnlyList<PurchaseOrderLine> Lines => _lines.AsReadOnly();

    private PurchaseOrder() { }

    /// <summary>
    /// Build a Draft PO. Validation only — the state machine lands in U2.
    /// </summary>
    public static Result<PurchaseOrder> Create(
        string supplierRef,
        DateTime expectedDeliveryAt,
        IEnumerable<(string Sku, int ExpectedQty)> lines
    )
    {
        ArgumentNullException.ThrowIfNull(lines);

        if (string.IsNullOrWhiteSpace(supplierRef))
        {
            return Result<PurchaseOrder>.Failure(
                "supplier_ref is required.",
                "po.supplier_ref_required"
            );
        }

        var lineList = lines.ToList();
        if (lineList.Count == 0)
        {
            return Result<PurchaseOrder>.Failure(
                "purchase order must have at least one line.",
                "po.no_lines"
            );
        }

        var po = new PurchaseOrder
        {
            SupplierRef = supplierRef.Trim(),
            ExpectedDeliveryAt = expectedDeliveryAt,
            Status = PurchaseOrderStatus.Draft,
        };

        foreach (var (sku, qty) in lineList)
        {
            var lineResult = PurchaseOrderLine.Create(po.Id, sku, qty);
            if (!lineResult.IsSuccess)
            {
                return Result<PurchaseOrder>.Failure(lineResult.Error!, lineResult.ErrorCode);
            }
            po._lines.Add(lineResult.Value!);
        }

        return Result<PurchaseOrder>.Success(po);
    }

    /// <summary>
    /// Draft → Open. Sprint-2-redux U2 body.
    /// </summary>
    public Result Open(DateTime now)
    {
        _ = now;
        throw new NotImplementedException(
            "Sprint-2-redux U2 behavior — see docs/plans/2026-05-13-001-feat-phase-1-sprint-2-redux-inbound-plan.md"
        );
    }

    /// <summary>
    /// Open → PartiallyReceived. Sprint-2-redux U2 body.
    /// </summary>
    public Result MarkPartiallyReceived()
    {
        throw new NotImplementedException(
            "Sprint-2-redux U2 behavior — see docs/plans/2026-05-13-001-feat-phase-1-sprint-2-redux-inbound-plan.md"
        );
    }

    /// <summary>
    /// Open or PartiallyReceived → Closed. Sprint-2-redux U2 body.
    /// </summary>
    public Result Close(DateTime now)
    {
        _ = now;
        throw new NotImplementedException(
            "Sprint-2-redux U2 behavior — see docs/plans/2026-05-13-001-feat-phase-1-sprint-2-redux-inbound-plan.md"
        );
    }

    /// <summary>
    /// Draft or Open → Cancelled. Sprint-2-redux U2 body.
    /// </summary>
    public Result Cancel(string reason, DateTime now)
    {
        _ = (reason, now);
        throw new NotImplementedException(
            "Sprint-2-redux U2 behavior — see docs/plans/2026-05-13-001-feat-phase-1-sprint-2-redux-inbound-plan.md"
        );
    }

    /// <summary>
    /// Apply a receiving to one line; updates the line's <c>ReceivedQty</c>
    /// and recomputes the PO status. Called by U3's Receiving flow.
    /// Sprint-2-redux U2/U3 body.
    /// </summary>
    public Result RecordLineReceipt(Guid lineId, int actualQty, DateTime now)
    {
        _ = (lineId, actualQty, now);
        throw new NotImplementedException(
            "Sprint-2-redux U2/U3 behavior — see docs/plans/2026-05-13-001-feat-phase-1-sprint-2-redux-inbound-plan.md"
        );
    }
}
