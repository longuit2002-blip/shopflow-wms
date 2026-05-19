using MediatR;
using ShopFlow.SharedKernel.Domain;

namespace ShopFlow.Inventory.Application.Commands;

/// <summary>
/// Set the low-stock threshold for a SKU (originally Sprint-6 plan U8 / R9).
///
/// Sprint-7.5 U3 — handler now persists to the real <c>skus.threshold</c>
/// column via <see cref="Ports.ISkuRepository"/>. The in-memory
/// <c>ISkuMetadataStore</c> singleton has been retired.
/// </summary>
public sealed record SetThresholdCommand(
    string Sku,
    int Threshold,
    string? IdempotencyKey) : IRequest<Result>;
