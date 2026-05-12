using Microsoft.EntityFrameworkCore;

namespace ShopFlow.Analytics.Infrastructure;

/// <summary>
/// Placeholder per-tenant read-side DbContext for the Analytics module
/// (plan U9). Real projections (event-sourced aggregations over the
/// Inventory + Outbound write streams) land in Phase-2. Per AGENTS.md
/// §11.76 Analytics has no Domain project; the read-side schema is
/// owned wholly by Infrastructure.
/// </summary>
public sealed class AnalyticsDbContext : DbContext
{
    public AnalyticsDbContext(DbContextOptions<AnalyticsDbContext> options)
        : base(options) { }
}
