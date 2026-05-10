using MediatR;
using ShopFlow.SharedKernel.Domain;

namespace ShopFlow.Inventory.Application.Queries;

/// <summary>
/// Compute current availability for a SKU as
/// <c>total − allocated − sum(active reservations)</c>. Per Tech Design
/// §7.5 the read goes through a repository join, not the StockItem
/// aggregate (which deliberately does not store the derived value).
/// </summary>
public sealed record GetAvailabilityQuery(string Sku) : IRequest<Result<AvailabilityDto>>;

/// <summary>
/// Read-side projection returned by <see cref="GetAvailabilityQuery"/>.
/// </summary>
public sealed record AvailabilityDto(
    string Sku,
    int TotalQuantity,
    int AllocatedQuantity,
    int ActiveReservationQuantity,
    int AvailableQuantity
);
