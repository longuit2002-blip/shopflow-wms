using MediatR;
using ShopFlow.Inventory.Domain;
using ShopFlow.SharedKernel.Domain;

namespace ShopFlow.Inventory.Application.Commands;

/// <summary>
/// Apply a positive or negative <paramref name="Delta"/> to the on-hand
/// quantity of <paramref name="Sku"/>. <paramref name="Reason"/> classifies
/// the adjustment for the <c>stock_adjustments</c> audit log;
/// <paramref name="UserId"/> identifies the operator (or the system actor
/// for automated reconciliations).
/// </summary>
public sealed record AdjustStockCommand(
    string Sku,
    int Delta,
    StockAdjustmentReason Reason,
    Guid UserId
) : IRequest<Result>;
