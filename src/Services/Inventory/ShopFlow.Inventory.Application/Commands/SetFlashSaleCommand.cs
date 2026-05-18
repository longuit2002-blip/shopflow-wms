using MediatR;
using ShopFlow.Inventory.Application.Services;
using ShopFlow.SharedKernel.Application;
using ShopFlow.SharedKernel.Domain;

namespace ShopFlow.Inventory.Application.Commands;

/// <summary>
/// Toggle the <c>is_flash_sale</c> flag for a SKU (Sprint-6 plan U12 / R10).
/// Stored in-memory via <see cref="ISkuMetadataStore"/> until Sprint-7
/// adds the real <c>stock_items.is_flash_sale</c> column.
/// </summary>
public sealed record SetFlashSaleCommand(
    string Sku,
    bool Active,
    string? IdempotencyKey) : IRequest<Result>;

public sealed class SetFlashSaleCommandHandler(
    ISkuMetadataStore store,
    IRequestContext requestContext) : IRequestHandler<SetFlashSaleCommand, Result>
{
    private readonly ISkuMetadataStore store = store;
    private readonly IRequestContext requestContext = requestContext;

    public Task<Result> Handle(SetFlashSaleCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.Sku))
        {
            return Task.FromResult(Result.Failure("sku is required.", "stock.sku_required"));
        }
        this.store.SetFlashSale(this.requestContext.TenantSlug, request.Sku, request.Active);
        return Task.FromResult(Result.Success());
    }
}
