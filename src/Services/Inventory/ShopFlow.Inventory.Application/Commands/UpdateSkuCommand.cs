using MediatR;
using ShopFlow.Inventory.Application.Ports;
using ShopFlow.Inventory.Domain.Catalog;
using ShopFlow.Inventory.Domain.Catalog.ValueObjects;
using ShopFlow.SharedKernel.Domain;
using SkuCode = ShopFlow.Inventory.Domain.Sku;

namespace ShopFlow.Inventory.Application.Commands;

/// <summary>
/// Sprint-7.5 U4 — MediatR command for <c>PUT /api/v1/inventory/skus/{sku}</c>,
/// the Owner-facing "edit SKU" endpoint that the new EditSkuModal posts to.
/// Replaces the (existing or new) row with the 10-field rich payload via
/// <see cref="Ports.ISkuRepository.UpsertAsync"/>. Verb is PUT to match
/// the existing Sprint-6 <c>/threshold</c> + <c>/flash-sale</c> convention
/// (full-record replacement semantics).
/// </summary>
public sealed record UpdateSkuCommand(
    string Sku,
    string Name,
    string? Category,
    int? Threshold,
    int? WeightGrams,
    SkuDimensions? Dimensions,
    string? Description,
    string? ImageUrl,
    string? Barcode,
    string? Brand,
    bool IsFlashSale,
    string IdempotencyKey
) : IRequest<Result<SkuMutationResult>>;
