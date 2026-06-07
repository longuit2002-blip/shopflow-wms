using ShopFlow.SharedKernel.Domain;

namespace ShopFlow.Inbound.Domain;

/// <summary>
/// Append-only audit row written by the receiving flow when
/// <c>actual_qty != expected_qty</c> on a confirmed line per Sprint-2-redux
/// plan R9. Sprint-2-redux ships <c>Open</c> status only; resolution
/// workflow (closing the ticket, retroactive PO adjustment) is deferred to
/// Sprint-2.5 or Phase-2 admin surfaces.
/// </summary>
/// <remarks>
/// <para>Separate aggregate from <see cref="Receiving"/> / <see cref="PurchaseOrder"/>
/// — the ticket is a log entry; closing it shouldn't have to load and save
/// the original receiving aggregate. Carries the identifiers needed for
/// the eventual resolution workflow to navigate back to the source.</para>
///
/// <para>Overage (<c>actual_qty &gt; expected_qty</c>) gets the same shape
/// of ticket as underage — the operator deals with both via the same
/// resolution flow when it lands.</para>
/// </remarks>
public sealed class ReconciliationTicket : BaseEntity
{
    public Guid PurchaseOrderId { get; private set; }

    public Guid PurchaseOrderLineId { get; private set; }

    public Guid ReceivingId { get; private set; }

    public string Sku { get; private set; } = string.Empty;

    public int ExpectedQty { get; private set; }

    public int ActualQty { get; private set; }

    public int VarianceQty => ActualQty - ExpectedQty;

    public ReconciliationTicketStatus Status { get; private set; } =
        ReconciliationTicketStatus.Open;

    public DateTime OccurredAt { get; private set; }

    private ReconciliationTicket() { }

    /// <summary>
    /// Record a mismatch as an Open ticket. No state machine in Sprint-2-redux —
    /// the ticket stays Open. Resolution lands in a follow-up sprint.
    /// </summary>
    public static Result<ReconciliationTicket> Open(
        Guid purchaseOrderId,
        Guid purchaseOrderLineId,
        Guid receivingId,
        string sku,
        int expectedQty,
        int actualQty,
        DateTime occurredAt
    )
    {
        if (expectedQty == actualQty)
        {
            return Result<ReconciliationTicket>.Failure(
                "reconciliation ticket requires expected != actual.",
                "ticket.no_variance"
            );
        }
        if (string.IsNullOrWhiteSpace(sku))
        {
            return Result<ReconciliationTicket>.Failure("sku is required.", "ticket.sku_required");
        }
        return Result<ReconciliationTicket>.Success(
            new ReconciliationTicket
            {
                PurchaseOrderId = purchaseOrderId,
                PurchaseOrderLineId = purchaseOrderLineId,
                ReceivingId = receivingId,
                Sku = sku.Trim(),
                ExpectedQty = expectedQty,
                ActualQty = actualQty,
                Status = ReconciliationTicketStatus.Open,
                OccurredAt = occurredAt,
            }
        );
    }
}
