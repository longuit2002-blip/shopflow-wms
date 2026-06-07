using MediatR;
using ShopFlow.Inventory.Application.Dtos;

namespace ShopFlow.Inventory.Application.Queries;

/// <summary>
/// MediatR query for <c>GET /api/v1/inventory/summary</c> — KPI strip
/// aggregates (Sprint-6 plan U7 / R21 Backend Gap closure).
/// </summary>
public sealed record GetInventorySummaryQuery() : IRequest<InventorySummaryDto>;
