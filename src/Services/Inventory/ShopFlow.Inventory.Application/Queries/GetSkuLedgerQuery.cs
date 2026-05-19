using MediatR;
using ShopFlow.Inventory.Application.Dtos;

namespace ShopFlow.Inventory.Application.Queries;

/// <summary>
/// MediatR query for <c>GET /api/v1/inventory/skus/{sku}/ledger</c> — the
/// reservation ledger entries for one SKU, ordered DESC by event time
/// with a server-computed cumulative balance.
///
/// Sprint-7.5 U6 added opaque base64 cursor pagination. The cursor encodes
/// the last row's <c>(occurredAt, id)</c>; the handler resumes the DESC
/// scan past that point via Postgres row-value comparison. Default page
/// size 50, clamps to [1, 200]. The returned <see cref="SkuLedgerDto"/>
/// carries a non-null <c>NextCursor</c> when more rows remain.
/// </summary>
public sealed record GetSkuLedgerQuery(string Sku, int Limit = 50, string? Cursor = null)
    : IRequest<SkuLedgerDto>;
