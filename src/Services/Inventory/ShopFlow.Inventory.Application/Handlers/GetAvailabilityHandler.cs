using MediatR;
using ShopFlow.Inventory.Application.Ports;
using ShopFlow.Inventory.Application.Queries;
using ShopFlow.Inventory.Domain;
using ShopFlow.SharedKernel.Application;
using ShopFlow.SharedKernel.Domain;

namespace ShopFlow.Inventory.Application.Handlers;

/// <summary>
/// Resolves <see cref="GetAvailabilityQuery"/> via
/// <see cref="IStockItemRepository.GetAvailabilityAsync"/>. Returns
/// <see cref="Result{T}.Failure"/> with code <c>"NOT_FOUND"</c> when the
/// SKU does not exist for the active tenant.
/// </summary>
public sealed class GetAvailabilityHandler
    : IRequestHandler<GetAvailabilityQuery, Result<AvailabilityDto>>
{
    private readonly IStockItemRepository _stockItems;
    private readonly IRequestContext _requestContext;

    public GetAvailabilityHandler(IStockItemRepository stockItems, IRequestContext requestContext)
    {
        _stockItems = stockItems;
        _requestContext = requestContext;
    }

    public async Task<Result<AvailabilityDto>> Handle(
        GetAvailabilityQuery query,
        CancellationToken cancellationToken
    )
    {
        var sku = new Sku(query.Sku);
        var dto = await _stockItems
            .GetAvailabilityAsync(_requestContext.TenantId, sku, cancellationToken)
            .ConfigureAwait(false);

        return dto is null
            ? Result<AvailabilityDto>.Failure($"Stock item '{query.Sku}' not found.", "NOT_FOUND")
            : Result<AvailabilityDto>.Success(dto);
    }
}
