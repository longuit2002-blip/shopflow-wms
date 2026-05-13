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
/// U1 ships the type shape only. <c>Create</c> + validation land in U2.
/// </remarks>
public sealed class OrderLine : BaseEntity
{
    public Guid OrderId { get; private set; }

    public string Sku { get; private set; } = string.Empty;

    public int Qty { get; private set; }

    public int? ExpectedWeight { get; private set; }

    private OrderLine() { }
}
