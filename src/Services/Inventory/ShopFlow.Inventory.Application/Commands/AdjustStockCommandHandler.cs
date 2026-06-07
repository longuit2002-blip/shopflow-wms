using MediatR;
using ShopFlow.Inventory.Application.Ports;
using ShopFlow.Inventory.Domain;
using ShopFlow.SharedKernel.Domain;

namespace ShopFlow.Inventory.Application.Commands;

/// <summary>
/// MediatR handler for <see cref="AdjustStockCommand"/> — Sprint-6 plan U8.
///
/// Validates the SKU string + parses the reason enum, then delegates to
/// <see cref="IStockItemRepository.AdjustAsync"/> which applies the delta,
/// appends the audit row, and emits a <c>StockLevelChangedV1</c> outbox
/// message in one transaction (Sprint-5 U2 path).
/// </summary>
public sealed class AdjustStockCommandHandler(IStockItemRepository repository)
    : IRequestHandler<AdjustStockCommand, Result>
{
    private readonly IStockItemRepository repository = repository;

    public async Task<Result> Handle(
        AdjustStockCommand request,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrWhiteSpace(request.Sku))
        {
            return Result.Failure("sku is required.", "stock.adjustment_sku_required");
        }

        if (!Enum.TryParse<StockAdjustmentReason>(request.Reason, ignoreCase: true, out var reason))
        {
            return Result.Failure(
                $"unknown reason '{request.Reason}'. Valid: {string.Join(", ", Enum.GetNames<StockAdjustmentReason>())}.",
                "stock.adjustment_reason_invalid"
            );
        }

        var sku = Sku.Create(request.Sku);
        return await this
            .repository.AdjustAsync(sku, request.Delta, reason, request.Note, cancellationToken)
            .ConfigureAwait(false);
    }
}
