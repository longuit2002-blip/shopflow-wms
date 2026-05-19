using MediatR;
using ShopFlow.Inventory.Application.Ports;
using ShopFlow.SharedKernel.Domain;
using SkuCode = ShopFlow.Inventory.Domain.Sku;

namespace ShopFlow.Inventory.Application.Commands;

/// <summary>
/// MediatR handler for <see cref="SetThresholdCommand"/> — originally
/// Sprint-6 plan U8 against the in-memory <c>ISkuMetadataStore</c>;
/// Sprint-7.5 U3 rewires the handler to the real <see cref="ISkuRepository"/>
/// catalog so threshold survives an Inventory.Api restart.
/// </summary>
/// <remarks>
/// The repository's <c>UpdateThresholdAsync</c> handles the create-or-
/// update branching: when no <c>skus</c> row exists yet the repository
/// creates a minimal one (name defaults to the SKU code) so the inline
/// threshold-edit path does not also force the user through the Create
/// SKU modal. The user can rename the row later via Sprint-7.5 U4's
/// edit modal.
/// </remarks>
public sealed class SetThresholdCommandHandler(
    ISkuRepository skuRepository) : IRequestHandler<SetThresholdCommand, Result>
{
    private readonly ISkuRepository skuRepository = skuRepository;

    public async Task<Result> Handle(SetThresholdCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrWhiteSpace(request.Sku))
        {
            return Result.Failure("sku is required.", "stock.sku_required");
        }
        if (request.Threshold < 0)
        {
            return Result.Failure(
                "threshold must be >= 0.",
                "stock.threshold_negative"
            );
        }

        SkuCode code;
        try
        {
            code = SkuCode.Create(request.Sku);
        }
        catch (ArgumentException ex)
        {
            return Result.Failure(ex.Message, "stock.sku_invalid");
        }

        var result = await this.skuRepository
            .UpdateThresholdAsync(code, request.Threshold, cancellationToken)
            .ConfigureAwait(false);

        return result.IsSuccess
            ? Result.Success()
            : Result.Failure(result.Error!, result.ErrorCode);
    }
}
