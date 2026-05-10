using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace ShopFlow.Analytics.Infrastructure;

/// <summary>
/// Analytics module composition root. Phase-0 stub; concrete registrations
/// (read-model DbContext, integration-event consumers that project into
/// reporting tables, query handlers) land in Phase-3 Sprint-7 (W9) per
/// docs/plans/2026-04-27-001-feat-shopflow-wms-phase-0-bootstrap-plan.md.
/// Per Tech Design §5, Analytics is read-side only — no Domain layer.
/// </summary>
public static class AnalyticsServiceCollectionExtensions
{
    public static IServiceCollection AddAnalyticsModule(
        this IServiceCollection services,
        IConfiguration configuration
    )
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        _ = configuration;
        return services;
    }
}
