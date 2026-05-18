using MediatR;
using ShopFlow.Inventory.Application.Dtos;

namespace ShopFlow.Inventory.Application.Queries;

/// <summary>
/// MediatR query for <c>GET /api/v1/inventory/skus/{sku}/ledger</c> — the
/// reservation ledger entries for one SKU, ordered DESC by event time
/// with a server-computed cumulative balance (Sprint-6 plan U7 / R6).
/// </summary>
public sealed record GetSkuLedgerQuery(string Sku, int Limit = 100)
    : IRequest<SkuLedgerDto>;
