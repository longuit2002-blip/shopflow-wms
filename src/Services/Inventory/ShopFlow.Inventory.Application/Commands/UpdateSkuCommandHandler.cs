using MediatR;
using ShopFlow.Inventory.Application.Ports;
using ShopFlow.Inventory.Domain.Catalog;
using ShopFlow.SharedKernel.Domain;
using SkuCode = ShopFlow.Inventory.Domain.Sku;

namespace ShopFlow.Inventory.Application.Commands;

/// <summary>
/// Sprint-7.5 U4 — handles <see cref="UpdateSkuCommand"/>. Constructs the
/// SKU aggregate from the 10-field payload (validators on the aggregate
/// factory enforce sku regex, name presence, threshold ≥ 0, weight ≥ 0,
/// etc.), then defers to <see cref="ISkuRepository.UpsertAsync"/> which
/// returns (Sku, bool changed). Idempotency is natural via the table's
/// PK on Sku — repeated PUTs with the same payload are no-ops.
/// </summary>
public sealed class UpdateSkuCommandHandler(ISkuRepository skus)
    : IRequestHandler<UpdateSkuCommand, Result<SkuMutationResult>>
{
    private readonly ISkuRepository _skus = skus;

    public async Task<Result<SkuMutationResult>> Handle(
        UpdateSkuCommand request,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(request);

        SkuCode codeValue;
        try
        {
            codeValue = SkuCode.Create(request.Sku);
        }
        catch (ArgumentException ex)
        {
            return Result<SkuMutationResult>.Failure(ex.Message, "sku.invalid_format");
        }

        var build = Sku.Create(
            code: codeValue,
            name: request.Name,
            category: request.Category,
            threshold: request.Threshold,
            weightGrams: request.WeightGrams,
            dimensions: request.Dimensions,
            description: request.Description,
            imageUrl: request.ImageUrl,
            barcode: request.Barcode,
            brand: request.Brand,
            isFlashSale: request.IsFlashSale
        );

        if (!build.IsSuccess)
        {
            return Result<SkuMutationResult>.Failure(build.Error!, build.ErrorCode);
        }

        var result = await _skus.UpsertAsync(build.Value!, cancellationToken).ConfigureAwait(false);
        return Result<SkuMutationResult>.Success(result);
    }
}
