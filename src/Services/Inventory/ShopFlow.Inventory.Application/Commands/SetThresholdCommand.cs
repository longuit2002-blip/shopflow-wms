using MediatR;
using ShopFlow.SharedKernel.Domain;

namespace ShopFlow.Inventory.Application.Commands;

/// <summary>
/// Set the low-stock threshold for a SKU (Sprint-6 plan U8 / R9).
///
/// Stored in-process via <see cref="Services.ISkuMetadataStore"/> until
/// Sprint-7 adds a real <c>stock_items.threshold</c> column.
/// </summary>
public sealed record SetThresholdCommand(
    string Sku,
    int Threshold,
    string? IdempotencyKey) : IRequest<Result>;
