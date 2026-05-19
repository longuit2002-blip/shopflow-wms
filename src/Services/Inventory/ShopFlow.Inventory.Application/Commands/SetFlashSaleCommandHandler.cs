using MediatR;
using ShopFlow.Inventory.Application.Ports;
using ShopFlow.SharedKernel.Domain;
using SkuCode = ShopFlow.Inventory.Domain.Sku;

namespace ShopFlow.Inventory.Application.Commands;

/// <summary>
/// MediatR handler for <see cref="SetFlashSaleCommand"/> — originally
/// Sprint-6 plan U12 against the in-memory <c>ISkuMetadataStore</c>;
/// Sprint-7.5 U3 rewires it to the real <see cref="ISkuRepository"/>
/// catalog. Sprint-7.5 U5 will extend the repository's
/// <c>UpdateFlashSaleAsync</c> call site to emit a
/// <c>SkuFlashSaleChangedV1</c> outbox event when the <c>Changed</c>
/// flag is <c>true</c>; U3 leaves that seam unwired (the handler does
/// not yet inspect <c>Changed</c>).
/// </summary>
/// <remarks>
/// <para>Per ADV-004 the original Sprint-6 implementation packed both
/// the command record and the handler class into <c>SetFlashSaleCommand.cs</c>
/// — non-standard layout. U3 lifts the handler to its own file so the
/// shape matches <see cref="SetThresholdCommandHandler"/> +
/// <see cref="AdjustStockCommandHandler"/> + <see cref="CreateSkuCommandHandler"/>.</para>
/// </remarks>
public sealed class SetFlashSaleCommandHandler(
    ISkuRepository skuRepository) : IRequestHandler<SetFlashSaleCommand, Result>
{
    private readonly ISkuRepository skuRepository = skuRepository;

    public async Task<Result> Handle(SetFlashSaleCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrWhiteSpace(request.Sku))
        {
            return Result.Failure("sku is required.", "stock.sku_required");
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
            .UpdateFlashSaleAsync(code, request.Active, cancellationToken)
            .ConfigureAwait(false);

        // U3 leaves the outbox-emit seam unwired — U5 will hook
        // SkuFlashSaleChangedV1 here gated on result.Value.Changed.
        return result.IsSuccess
            ? Result.Success()
            : Result.Failure(result.Error!, result.ErrorCode);
    }
}
