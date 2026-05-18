using MediatR;
using ShopFlow.SharedKernel.Domain;

namespace ShopFlow.Inventory.Application.Commands;

/// <summary>
/// Create a new SKU with an initial available count (Sprint-6 plan U8 / R11).
///
/// Sprint-6 scope: ships the SKU + initial available. Plan-listed extras
/// (name, category, threshold, price, cost, channel allocations) wait
/// for Sprint-7's schema expansion; the frontend Create modal in U12
/// collects them but the backend currently discards them.
/// </summary>
public sealed record CreateSkuCommand(
    string Sku,
    int InitialAvailable,
    string? IdempotencyKey) : IRequest<Result>;
