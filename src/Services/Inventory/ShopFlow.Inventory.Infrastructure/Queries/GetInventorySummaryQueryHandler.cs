using MediatR;
using Microsoft.EntityFrameworkCore;
using ShopFlow.Inventory.Application.Dtos;
using ShopFlow.Inventory.Application.Queries;

namespace ShopFlow.Inventory.Infrastructure.Queries;

/// <summary>
/// MediatR handler for <see cref="GetInventorySummaryQuery"/> — Sprint-6
/// plan U7 / R21 Backend Gap closure.
///
/// Single-trip aggregate query so the Inventory screen's 2-second polling
/// loop doesn't N+1 across the SKU table. The dev-machine dataset is
/// small enough that a SCAN of <c>stock_items</c> is fine; production
/// scale considerations live in Sprint-7 (covering index + materialised
/// view).
///
/// Sprint-6 caveats reflected in the DTO:
///   - <c>BelowThresholdCount</c> always returns 0 (no threshold column
///     in Sprint-6 schema; Sprint-7 adds it).
///   - <c>OversellRiskCount</c> = SKUs where <c>reserved &gt; available</c>.
///     The conditional-CTE INSERT prevents true oversold rows; this
///     surfaces SKUs where one more reservation would tip into the
///     danger zone.
/// </summary>
public sealed class GetInventorySummaryQueryHandler(InventoryDbContext db)
    : IRequestHandler<GetInventorySummaryQuery, InventorySummaryDto>
{
    private readonly InventoryDbContext db = db;

    public async Task<InventorySummaryDto> Handle(
        GetInventorySummaryQuery request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var aggregate = await this.db.StockItems
            .AsNoTracking()
            .GroupBy(_ => 1)
            .Select(g => new
            {
                Total = g.Count(),
                TotalAvailable = g.Sum(s => (long)s.Available.Value),
                TotalReserved = g.Sum(s => (long)s.Reserved.Value),
                OversellRiskCount = g.Count(s => s.Reserved.Value > s.Available.Value),
            })
            .SingleOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        if (aggregate is null)
        {
            return new InventorySummaryDto(0, 0, 0, 0, 0);
        }

        return new InventorySummaryDto(
            TotalSkus: aggregate.Total,
            TotalAvailable: aggregate.TotalAvailable,
            TotalReserved: aggregate.TotalReserved,
            BelowThresholdCount: 0, // Sprint-7 adds the threshold column
            OversellRiskCount: aggregate.OversellRiskCount);
    }
}
