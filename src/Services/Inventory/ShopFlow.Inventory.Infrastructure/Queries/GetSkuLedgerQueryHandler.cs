using MediatR;
using Microsoft.EntityFrameworkCore;
using ShopFlow.Inventory.Application.Dtos;
using ShopFlow.Inventory.Application.Queries;
using ShopFlow.Inventory.Domain;

namespace ShopFlow.Inventory.Infrastructure.Queries;

/// <summary>
/// MediatR handler for <see cref="GetSkuLedgerQuery"/> — Sprint-6 plan U7.
///
/// Reads from <c>reservations_ledger</c> filtered by SKU. Returns entries
/// ordered DESC by event time (most-recent first) with a running balance
/// computed server-side. Sprint-3-redux split the ledger by order_line_id;
/// rows that don't carry one default to "_default" in the DTO.
///
/// Sprint-6 caps the returned set at <c>request.Limit</c> (default 100;
/// clamped to [1, 500]). Cursor pagination is deferred — the drawer in
/// U10 only renders the most-recent N entries; older history is a
/// Sprint-7 polish.
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

        var limit = Math.Clamp(request.Limit, 1, 500);
        var skuValue = Sku.Create(request.Sku);

        var rows = await this.db.Reservations
            .AsNoTracking()
            .Where(r => r.Sku == skuValue)
            .OrderByDescending(r => r.CreatedAt)
            .Take(limit)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

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

        return new SkuLedgerDto(Items: withBalance, NextCursor: null);
    }
}
