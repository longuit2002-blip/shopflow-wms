using ShopFlow.SharedKernel.Domain;

namespace ShopFlow.Outbound.Domain;

/// <summary>
/// Child entity of <see cref="Order"/> per Sprint-3-redux plan R2. Each
/// line carries the SKU + quantity for the reservation, and an optional
/// per-line expected weight that feeds <see cref="Order.ExpectedWeightTotal"/>
/// for the pack-time weight check (U6). The line id (Guid) is the
/// <c>order_line_id</c> on the Inventory ledger's composite UNIQUE
/// <c>(order_id, order_line_id)</c> per K10/K11.
/// </summary>
/// <remarks>
/// Part of the <see cref="Order"/> aggregate — no independent repository,
/// no independent lifecycle. <see cref="Create"/> stays internal: only
/// <c>Order.Create</c> can produce a new line so the parent stays the
/// gatekeeper for the composite UNIQUE id allocation.
/// </remarks>
public sealed class OrderLine : BaseEntity
{
    public Guid OrderId { get; private set; }

    public string Sku { get; private set; } = string.Empty;

    public int Qty { get; private set; }

    public int? ExpectedWeight { get; private set; }

    private OrderLine() { }

    internal static Result<OrderLine> Create(Guid orderId, string sku, int qty, int? expectedWeight)
    {
        if (string.IsNullOrWhiteSpace(sku))
        {
            return Result<OrderLine>.Failure("sku is required.", "order_line.sku_required");
        }
        if (qty <= 0)
        {
            return Result<OrderLine>.Failure("qty must be > 0.", "order_line.qty_non_positive");
        }
        if (expectedWeight is < 0)
        {
            return Result<OrderLine>.Failure(
                "expected_weight must be >= 0 when present.",
                "order_line.expected_weight_negative"
            );
        }

        return Result<OrderLine>.Success(
            new OrderLine
            {
                OrderId = orderId,
                Sku = sku.Trim(),
                Qty = qty,
                ExpectedWeight = expectedWeight,
            }
        );
    }
}
