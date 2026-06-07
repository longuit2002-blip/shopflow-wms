using MediatR;
using ShopFlow.SharedKernel.Domain;

namespace ShopFlow.Inventory.Application.Commands;

/// <summary>
/// Toggle the <c>is_flash_sale</c> flag for a SKU (originally Sprint-6
/// plan U12 / R10). Sprint-7.5 U3 promotes the flag from the in-memory
/// <c>ISkuMetadataStore</c> to a real <c>skus.is_flash_sale</c> column;
/// the handler now lives in its own file (<c>SetFlashSaleCommandHandler.cs</c>)
/// alongside <c>SetThresholdCommandHandler.cs</c>.
/// </summary>
public sealed record SetFlashSaleCommand(string Sku, bool Active, string? IdempotencyKey)
    : IRequest<Result>;
