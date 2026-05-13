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
    /// Draft → Open. Stamps <see cref="OpenedAt"/>.
    /// </summary>
    public Result Open(DateTime now)
    {
        if (Status == PurchaseOrderStatus.Open)
        {
            return Result.Failure("already open.", "po.already_open");
        }
        if (Status != PurchaseOrderStatus.Draft)
        {
            return Result.Failure(
                $"cannot open PO in {Status} state; only Draft is openable.",
                "po.invalid_state"
            );
        }
        Status = PurchaseOrderStatus.Open;
        OpenedAt = now;
        UpdatedAt = now;
        return Result.Success();
    }

    /// <summary>
    /// Open → PartiallyReceived. Idempotent in the sense that a second
    /// call from already-PartiallyReceived is a no-op success.
    /// </summary>
    public Result MarkPartiallyReceived()
    {
        if (Status == PurchaseOrderStatus.PartiallyReceived)
        {
            return Result.Success();
        }
        if (Status != PurchaseOrderStatus.Open)
        {
            return Result.Failure(
                $"cannot mark partially-received from {Status}; only Open is eligible.",
                "po.invalid_state"
            );
        }
        Status = PurchaseOrderStatus.PartiallyReceived;
        UpdatedAt = DateTime.UtcNow;
        return Result.Success();
    }

    /// <summary>
    /// Open or PartiallyReceived → Closed. Requires every line fully
    /// received (received_qty &gt;= expected_qty); overage counts as
    /// fully received because the reconciliation ticket has captured
    /// the surplus per Sprint-2-redux plan R8.
    /// </summary>
    public Result Close(DateTime now)
    {
        if (Status == PurchaseOrderStatus.Closed)
        {
            return Result.Failure("already closed.", "po.already_closed");
        }
        if (
            Status != PurchaseOrderStatus.Open
            && Status != PurchaseOrderStatus.PartiallyReceived
        )
        {
            return Result.Failure(
                $"cannot close PO in {Status} state.",
                "po.invalid_state"
            );
        }
        if (_lines.Any(l => l.ReceivedQty < l.ExpectedQty))
        {
            return Result.Failure(
                "cannot close PO with under-received lines.",
                "po.not_fully_received"
            );
        }
        Status = PurchaseOrderStatus.Closed;
        ClosedAt = now;
        UpdatedAt = now;
        return Result.Success();
    }

    /// <summary>
    /// Draft or Open → Cancelled. Records the reason on the aggregate.
    /// </summary>
    public Result Cancel(string reason, DateTime now)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            return Result.Failure(
                "cancellation reason is required.",
                "po.cancel_reason_required"
            );
        }
        if (Status == PurchaseOrderStatus.Cancelled)
        {
            return Result.Failure("already cancelled.", "po.already_cancelled");
        }
        if (Status != PurchaseOrderStatus.Draft && Status != PurchaseOrderStatus.Open)
        {
            return Result.Failure(
                $"cannot cancel PO in {Status} state.",
                "po.invalid_state"
            );
        }
        Status = PurchaseOrderStatus.Cancelled;
        CancellationReason = reason.Trim();
        CancelledAt = now;
        UpdatedAt = now;
        return Result.Success();
    }

    /// <summary>
    /// Apply a receiving to one line; updates the line's <c>ReceivedQty</c>
    /// and recomputes the PO status per Sprint-2-redux plan R8. Auto-
    /// transitions:
    /// <list type="bullet">
    ///   <item><description>Open → PartiallyReceived when any line has received_qty &gt; 0 and at least one is still under-received.</description></item>
    ///   <item><description>PartiallyReceived → Closed when every line has received_qty &gt;= expected_qty.</description></item>
    /// </list>
    /// Overage counts as fully received per R8.
    /// </summary>
    public Result RecordLineReceipt(Guid lineId, int actualQty, DateTime now)
    {
        if (Status != PurchaseOrderStatus.Open && Status != PurchaseOrderStatus.PartiallyReceived)
        {
            return Result.Failure(
                $"cannot receive against PO in {Status} state.",
                "po.invalid_state"
            );
        }
        var line = _lines.FirstOrDefault(l => l.Id == lineId);
        if (line is null)
        {
            return Result.Failure(
                $"line {lineId} not found on PO {Id}.",
                "po.line_not_found"
            );
        }
        var receipt = line.RecordReceipt(actualQty, now);
        if (!receipt.IsSuccess)
        {
            return receipt;
        }

        var allFullyReceived = _lines.All(l => l.ReceivedQty >= l.ExpectedQty);
        var anyReceived = _lines.Any(l => l.ReceivedQty > 0);
        if (allFullyReceived)
        {
            Status = PurchaseOrderStatus.Closed;
            ClosedAt = now;
        }
        else if (anyReceived && Status == PurchaseOrderStatus.Open)
        {
            Status = PurchaseOrderStatus.PartiallyReceived;
        }
        UpdatedAt = now;
        return Result.Success();
    }
}
