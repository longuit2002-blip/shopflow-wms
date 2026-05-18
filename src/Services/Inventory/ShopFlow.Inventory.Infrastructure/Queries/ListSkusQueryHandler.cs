using MediatR;
using Microsoft.EntityFrameworkCore;
using ShopFlow.Inventory.Application.Dtos;
using ShopFlow.Inventory.Application.Queries;
using ShopFlow.Inventory.Application.Services;
using ShopFlow.Inventory.Domain;

namespace ShopFlow.Inventory.Infrastructure.Queries;

/// <summary>
/// MediatR handler for <see cref="ListSkusQuery"/> — Sprint-6 plan U7.
///
/// Reads paginated rows from <c>stock_items</c>. Filter and ordering are
/// kept minimal in Sprint-6:
///   - Search is a case-insensitive substring match on SKU.
///   - Ordering is by SKU ascending (stable for deterministic UI rows).
///   - Total count is a separate query inside the same transaction so the
///     pagination footer renders correctly.
///
/// Threshold + isFlashSale come from the in-memory metadata store
/// (Sprint-6 U8 — <see cref="ISkuMetadataReader"/>). Sprint-7 promotes
/// them to real columns. Channel allocations + p24 outbound still ship
/// empty (cross-module join is Sprint-7).
/// </summary>
public sealed class ListSkusQueryHandler(
    InventoryDbContext db,
    ISkuMetadataReader metadata)
    : IRequestHandler<ListSkusQuery, PaginatedSkuListDto>
{
    private readonly InventoryDbContext db = db;
    private readonly ISkuMetadataReader metadata = metadata;

    public async Task<PaginatedSkuListDto> Handle(
        ListSkusQuery request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var pageSize = Math.Clamp(request.PageSize, 1, 200);
        var page = Math.Max(1, request.Page);
        var skip = (page - 1) * pageSize;

        IQueryable<StockItem> query = this.db.StockItems.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var s = request.Search.Trim();
            query = query.Where(it => EF.Functions.ILike(((string)(object)it.Sku), $"%{s}%"));
        }

        var total = await query.CountAsync(cancellationToken).ConfigureAwait(false);

        var rows = await query
            .OrderBy(it => it.Sku)
            .Skip(skip)
            .Take(pageSize)
            .Select(it => new
            {
                Sku = it.Sku.Value,
                Available = it.Available.Value,
                Reserved = it.Reserved.Value,
            })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var items = rows
            .Select(r => new SkuListItemDto(
                Sku: r.Sku,
                Available: r.Available,
                Reserved: r.Reserved,
                Name: r.Sku, // Sprint-7 reads from a real name column
                Category: null,
                Threshold: this.metadata.GetThreshold(r.Sku),
                IsFlashSale: this.metadata.IsFlashSale(r.Sku),
                Allocations: Array.Empty<ChannelAllocationDto>(),
                P24Outbound: 0))
            .ToList();

        return new PaginatedSkuListDto(items, page, pageSize, total);
    }
}
