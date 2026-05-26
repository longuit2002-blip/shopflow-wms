using MediatR;
using ShopFlow.Inventory.Application.Ports;
using ShopFlow.Inventory.Domain;
using ShopFlow.SharedKernel.Domain;

namespace ShopFlow.Inventory.Application.Commands;

/// <summary>
/// MediatR handler for <see cref="CreateSkuCommand"/> — Sprint-6 plan U8.
///
/// Rejects when the SKU already exists (returns
/// <c>stock.sku_already_exists</c>); otherwise creates a new
/// <see cref="StockItem"/> via the aggregate factory and persists it.
/// The PK is the SKU itself so duplicate inserts naturally trip 23505
/// at the DB layer; this handler short-circuits earlier for a cleaner
/// error code.
/// </summary>
public sealed class CreateSkuCommandHandler(IStockItemRepository repository)
    : IRequestHandler<CreateSkuCommand, Result>
{
    private readonly IStockItemRepository repository = repository;

    public async Task<Result> Handle(CreateSkuCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrWhiteSpace(request.Sku))
        {
            return Result.Failure("sku is required.", "stock.sku_required");
        }

        if (request.InitialAvailable < 0)
        {
            return Result.Failure(
                "initialAvailable must be ≥ 0.",
                "stock.initial_available_negative"
            );
        }

        var sku = Sku.Create(request.Sku);
        var existing = await this
            .repository.FindBySkuAsync(sku, cancellationToken)
            .ConfigureAwait(false);
        if (existing is not null)
        {
            return Result.Failure($"sku '{sku.Value}' already exists.", "stock.sku_already_exists");
        }

        var item = StockItem.Create(sku, Quantity.From(request.InitialAvailable));
        await this.repository.AddAsync(item, cancellationToken).ConfigureAwait(false);
        return Result.Success();
    }
}
