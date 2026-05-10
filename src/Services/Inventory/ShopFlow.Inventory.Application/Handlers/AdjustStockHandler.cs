using MediatR;
using Microsoft.EntityFrameworkCore;
using ShopFlow.Inventory.Application.Commands;
using ShopFlow.Inventory.Application.Ports;
using ShopFlow.Inventory.Domain;
using ShopFlow.SharedKernel.Application;
using ShopFlow.SharedKernel.Domain;

namespace ShopFlow.Inventory.Application.Handlers;

/// <summary>
/// Handles <see cref="AdjustStockCommand"/> by loading the StockItem
/// aggregate, calling <see cref="StockItem.AdjustStock"/>, and saving via
/// the unit-of-work. The kernel's <c>OutboxInterceptor</c> persists the
/// raised <c>StockAdjustedEvent</c> in the same transaction.
/// </summary>
public sealed class AdjustStockHandler : IRequestHandler<AdjustStockCommand, Result>
{
    private readonly IStockItemRepository _stockItems;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IRequestContext _requestContext;

    public AdjustStockHandler(
        IStockItemRepository stockItems,
        IUnitOfWork unitOfWork,
        IRequestContext requestContext
    )
    {
        _stockItems = stockItems;
        _unitOfWork = unitOfWork;
        _requestContext = requestContext;
    }

    public async Task<Result> Handle(
        AdjustStockCommand command,
        CancellationToken cancellationToken
    )
    {
        var tenantId = _requestContext.TenantId;
        var sku = new Sku(command.Sku);

        var stockItem = await _stockItems
            .LoadBySkuAsync(tenantId, sku, cancellationToken)
            .ConfigureAwait(false);

        if (stockItem is null)
        {
            return Result.Failure($"Stock item '{command.Sku}' not found.", "NOT_FOUND");
        }

        stockItem.AdjustStock(command.Delta, command.Reason, command.UserId);
        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Result.Success();
    }
}
