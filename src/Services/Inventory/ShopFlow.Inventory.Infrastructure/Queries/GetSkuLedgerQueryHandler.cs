using MediatR;
using Microsoft.EntityFrameworkCore;
using ShopFlow.Inventory.Application.Dtos;
using ShopFlow.Inventory.Application.Queries;
using ShopFlow.Inventory.Domain;
using ShopFlow.Inventory.Infrastructure.Pagination;

namespace ShopFlow.Inventory.Infrastructure.Queries;

/// <summary>
/// MediatR handler for <see cref="GetSkuLedgerQuery"/>.
///
/// Reads from <c>reservations_ledger</c> filtered by SKU. Returns entries
/// ordered DESC by row <c>created_at</c> (most-recent first) with a
/// running balance computed server-side. Sprint-3-redux split the ledger
/// by order_line_id; rows that don't carry one default to "_default" in
/// the DTO.
///
/// Sprint-7.5 U6 added opaque base64 cursor pagination. The order key is
/// <c>(created_at DESC, id DESC)</c> backed by a new composite btree
/// index added in <c>20260519000008_AddReservationsLedgerSkuCreatedAtIndex</c>
/// — see KTD4. The plan named the index <c>(sku, occurred_at DESC)</c>
/// but the table has no <c>occurred_at</c> column; <c>created_at</c> is
/// the existing row-insert timestamp the original Sprint-6 handler
/// already ordered by, so the cursor + index align with that.
/// The wire-side <c>Timestamp</c> field continues to surface the
/// status-aware event time (<c>ConfirmedAt ?? ReleasedAt ?? ExpiredAt ?? CreatedAt</c>)
/// for the operator's UX.
///
/// Default page size 50; clamps to [1, 200].
/// </summary>
public sealed class GetSkuLedgerQueryHandler(InventoryDbContext db)
    : IRequestHandler<GetSkuLedgerQuery, SkuLedgerDto>
{
    private readonly InventoryDbContext db = db;

    public async Task<SkuLedgerDto> Handle(
        GetSkuLedgerQuery request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var limit = Math.Clamp(request.Limit, 1, 200);
        var skuValue = Sku.Create(request.Sku);

        // Decode cursor if present. A malformed cursor surfaces null here;
        // the controller maps the null to a 400 with a stable error code
        // before reaching the handler. By the time we run, the cursor
        // is either null (first page) or a validated payload.
        var cursor = OpaqueCursor.TryDecode(request.Cursor);
        var hasCursor = cursor is not null;

        // Fetch Limit+1 so we know whether a next page exists without a
        // second roundtrip.
        var query = this.db.Reservations
            .AsNoTracking()
            .Where(r => r.Sku == skuValue);

        if (hasCursor)
        {
            // Postgres row-value comparison: (created_at, id) < (cursor.OccurredAt, cursor.Id)
            // resumes the DESC scan past the cursor's tie-break point.
            // EF Core 9 translates the tuple-style predicate to a native
            // Postgres row-value compare on the composite index.
            var cursorCreatedAt = cursor!.OccurredAt;
            var cursorId = cursor.Id;
            query = query.Where(r =>
                r.CreatedAt < cursorCreatedAt
                || (r.CreatedAt == cursorCreatedAt && r.Id.CompareTo(cursorId) < 0));
        }

        var rows = await query
            .OrderByDescending(r => r.CreatedAt)
            .ThenByDescending(r => r.Id)
            .Take(limit + 1)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        // If we got Limit+1 rows, more remain. Trim to Limit and emit a
        // cursor pointing at the last *returned* row so the next call
        // resumes past it.
        string? nextCursor = null;
        if (rows.Count > limit)
        {
            rows = rows.Take(limit).ToList();
            var last = rows[^1];
            nextCursor = OpaqueCursor.Encode(new OpaqueCursorPayload(last.CreatedAt, last.Id));
        }

        // Running balance is most-useful when read chronologically. Build
        // ascending, accumulate, then flip DESC for the wire shape.
        var ascending = rows
            .Select(r =>
            {
                var ts = r.ConfirmedAt ?? r.ReleasedAt ?? r.ExpiredAt ?? r.CreatedAt;
                var signed = r.Status switch
                {
                    ReservationStatus.Confirmed
                        or ReservationStatus.Released
                        or ReservationStatus.Expired => -r.Quantity.Value,
                    _ => r.Quantity.Value, // Pending = held; appears as additive in the ledger view
                };
                return (Row: r, Timestamp: ts, Signed: signed);
            })
            .OrderBy(t => t.Timestamp)
            .ToList();

        var running = 0;
        var withBalance = ascending.Select(t =>
        {
            running += t.Signed;
            return new SkuLedgerEntryDto(
                Id: t.Row.Id,
                OrderId: t.Row.OrderId,
                OrderLineId: string.IsNullOrEmpty(t.Row.OrderLineId) ? "_default" : t.Row.OrderLineId,
                Status: t.Row.Status.ToString(),
                Quantity: t.Row.Quantity.Value,
                Timestamp: t.Timestamp,
                RunningBalance: running);
        }).ToList();

        // Wire shape: newest first.
        withBalance.Reverse();

        return new SkuLedgerDto(Items: withBalance, NextCursor: nextCursor);
    }
}
