using MediatR;
using Microsoft.EntityFrameworkCore;
using ShopFlow.Inventory.Application.Dtos;
using ShopFlow.Inventory.Application.Ports;
using ShopFlow.Inventory.Application.Queries;

namespace ShopFlow.Inventory.Infrastructure.Queries;

/// <summary>
/// MediatR handler for <see cref="GetInventorySummaryQuery"/> —
/// originally Sprint-6 plan U7 / R21. Single-trip read so the
/// Inventory screen's polling loop does not N+1 across the SKU table.
/// </summary>
/// <remarks>
/// Sprint-7.5 U3 — <c>BelowThresholdCount</c> now joins the real
/// <c>skus.threshold</c> column via
/// <see cref="ISkuRepository.GetAllThresholdsAsync"/> in one bulk
/// dictionary read; the singleton metadata reader has been removed.
/// </remarks>
public sealed class GetInventorySummaryQueryHandler(
    InventoryDbContext db,
    ISkuRepository skuRepository
) : IRequestHandler<GetInventorySummaryQuery, InventorySummaryDto>
{
    private readonly InventoryDbContext db = db;
    private readonly ISkuRepository skuRepository = skuRepository;

    public async Task<InventorySummaryDto> Handle(
        GetInventorySummaryQuery request,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(request);

        var rows = await this
            .db.StockItems.AsNoTracking()
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

        var thresholds = await this
            .skuRepository.GetAllThresholdsAsync(cancellationToken)
            .ConfigureAwait(false);

        long totalAvailable = 0;
        long totalReserved = 0;
        var oversellRisk = 0;
        var belowThreshold = 0;

        foreach (var r in rows)
        {
            totalAvailable += r.Available;
            totalReserved += r.Reserved;
            if (r.Reserved > r.Available)
                oversellRisk += 1;

            if (thresholds.TryGetValue(r.Sku, out var t) && r.Available < t)
            {
                belowThreshold += 1;
            }
        }

        return new InventorySummaryDto(
            TotalSkus: rows.Count,
            TotalAvailable: totalAvailable,
            TotalReserved: totalReserved,
            BelowThresholdCount: belowThreshold,
            OversellRiskCount: oversellRisk
        );
    }
}
