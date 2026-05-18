using MediatR;
using Microsoft.EntityFrameworkCore;
using ShopFlow.Inventory.Application.Dtos;
using ShopFlow.Inventory.Application.Queries;
using ShopFlow.Inventory.Application.Services;

namespace ShopFlow.Inventory.Infrastructure.Queries;

/// <summary>
/// MediatR handler for <see cref="GetInventorySummaryQuery"/> — Sprint-6
/// plan U7 / R21 Backend Gap closure.
///
/// Single-trip read so the Inventory screen's 2-second polling loop
/// doesn't N+1 across the SKU table. <c>BelowThresholdCount</c> joins
/// the in-memory threshold store (Sprint-6 U8 — Sprint-7 promotes to
/// an EF column).
/// </summary>
public sealed class GetInventorySummaryQueryHandler(
    InventoryDbContext db,
    ISkuMetadataReader metadata)
    : IRequestHandler<GetInventorySummaryQuery, InventorySummaryDto>
{
    private readonly InventoryDbContext db = db;
    private readonly ISkuMetadataReader metadata = metadata;

    public async Task<InventorySummaryDto> Handle(
        GetInventorySummaryQuery request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var rows = await this.db.StockItems
            .AsNoTracking()
            .Select(s => new
            {
                Sku = s.Sku.Value,
                Available = s.Available.Value,
                Reserved = s.Reserved.Value,
            })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        if (rows.Count == 0)
        {
            return new InventorySummaryDto(0, 0, 0, 0, 0);
        }

        long totalAvailable = 0;
        long totalReserved = 0;
        var oversellRisk = 0;
        var belowThreshold = 0;

        foreach (var r in rows)
        {
            totalAvailable += r.Available;
            totalReserved += r.Reserved;
            if (r.Reserved > r.Available) oversellRisk += 1;

            var threshold = this.metadata.GetThreshold(r.Sku);
            if (threshold is int t && r.Available < t) belowThreshold += 1;
        }

        return new InventorySummaryDto(
            TotalSkus: rows.Count,
            TotalAvailable: totalAvailable,
            TotalReserved: totalReserved,
            BelowThresholdCount: belowThreshold,
            OversellRiskCount: oversellRisk);
    }
}
