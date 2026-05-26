using MediatR;
using Microsoft.EntityFrameworkCore;
using ShopFlow.Inventory.Application.Dtos;
using ShopFlow.Inventory.Application.Ports;
using ShopFlow.Inventory.Application.Queries;
using ShopFlow.Inventory.Domain;

namespace ShopFlow.Inventory.Infrastructure.Queries;

/// <summary>
/// MediatR handler for <see cref="ListSkusQuery"/> — originally
/// Sprint-6 plan U7. Reads paginated rows from <c>stock_items</c>
/// and joins per-SKU rich-catalog metadata
/// (<see cref="ISkuRepository.GetListMetadataAsync"/>) in one bulk
/// round-trip per page rather than N per-row lookups.
/// </summary>
/// <remarks>
/// <para>Sprint-7.5 U3 — threshold + is_flash_sale + name + category
/// now come from the real <c>skus</c> table instead of the in-memory
/// metadata store. SKUs without a <c>skus</c> row fall back to the
/// SKU code as the name + null category + null threshold + false
/// is_flash_sale (matches the singleton's "unset" semantics).</para>
///
/// <para>Channel allocations + p24 outbound still ship empty
/// (Sprint-6 trade-off #3, unchanged in Sprint-7.5).</para>
/// </remarks>
public sealed class ListSkusQueryHandler(InventoryDbContext db, ISkuRepository skuRepository)
    : IRequestHandler<ListSkusQuery, PaginatedSkuListDto>
{
    private readonly InventoryDbContext db = db;
    private readonly ISkuRepository skuRepository = skuRepository;

    public async Task<PaginatedSkuListDto> Handle(
        ListSkusQuery request,
        CancellationToken cancellationToken
    )
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

        var skuCodes = rows.Select(r => r.Sku).ToList();
        var metadata = await this
            .skuRepository.GetListMetadataAsync(skuCodes, cancellationToken)
            .ConfigureAwait(false);

        var items = rows.Select(r =>
            {
                metadata.TryGetValue(r.Sku, out var m);
                return new SkuListItemDto(
                    Sku: r.Sku,
                    Available: r.Available,
                    Reserved: r.Reserved,
                    Name: m?.Name ?? r.Sku, // fallback to SKU code when no catalog row
                    Category: m?.Category,
                    Threshold: m?.Threshold,
                    IsFlashSale: m?.IsFlashSale ?? false,
                    Allocations: Array.Empty<ChannelAllocationDto>(),
                    P24Outbound: 0
                );
            })
            .ToList();

        return new PaginatedSkuListDto(items, page, pageSize, total);
    }
}
