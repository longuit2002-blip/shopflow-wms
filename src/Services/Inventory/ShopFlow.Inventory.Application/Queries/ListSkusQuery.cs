using MediatR;
using ShopFlow.Inventory.Application.Dtos;

namespace ShopFlow.Inventory.Application.Queries;

/// <summary>
/// MediatR query for <c>GET /api/v1/inventory/skus</c> — paginated SKU
/// table for the Inventory screen (Sprint-6 plan U7).
/// </summary>
/// <param name="Search">Optional case-insensitive substring match on SKU.</param>
/// <param name="Page">1-based page index.</param>
/// <param name="PageSize">Rows per page; clamped to [1, 200].</param>
public sealed record ListSkusQuery(string? Search = null, int Page = 1, int PageSize = 50)
    : IRequest<PaginatedSkuListDto>;
