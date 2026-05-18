using MediatR;
using ShopFlow.Inventory.Application.Services;
using ShopFlow.SharedKernel.Application;
using ShopFlow.SharedKernel.Domain;

namespace ShopFlow.Inventory.Application.Commands;

/// <summary>
/// MediatR handler for <see cref="SetThresholdCommand"/> — Sprint-6 plan U8.
/// Writes to the in-memory metadata store keyed by tenant slug + sku.
/// </summary>
public sealed class SetThresholdCommandHandler(
    ISkuMetadataStore store,
    IRequestContext requestContext) : IRequestHandler<SetThresholdCommand, Result>
{
    private readonly ISkuMetadataStore store = store;
    private readonly IRequestContext requestContext = requestContext;

    public Task<Result> Handle(SetThresholdCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.Sku))
        {
            return Task.FromResult(Result.Failure("sku is required.", "stock.sku_required"));
        }
        if (request.Threshold < 0)
        {
            return Task.FromResult(Result.Failure(
                "threshold must be ≥ 0.",
                "stock.threshold_negative"));
        }
        this.store.SetThreshold(this.requestContext.TenantSlug, request.Sku, request.Threshold);
        return Task.FromResult(Result.Success());
    }
}
