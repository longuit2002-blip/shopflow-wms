using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace ShopFlow.Inbound.Infrastructure;

/// <summary>
/// Inbound module composition root. Phase-0 stub; concrete registrations
/// (DbContext + Npgsql interceptors, entity configurations for
/// purchase_orders / receiving_records, MediatR handlers for
/// InboundConfirmed events) land in Phase-1 Sprint-2 (W4) per
/// docs/plans/2026-04-27-001-feat-shopflow-wms-phase-0-bootstrap-plan.md.
/// </summary>
public static class InboundServiceCollectionExtensions
{
    public static IServiceCollection AddInboundModule(
        this IServiceCollection services,
        IConfiguration configuration
    )
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        // Intentionally empty in Phase-0. The signature matches the canon
        // (AGENTS.md §11.76) so the composition root in Program.cs stays
        // identical across modules.
        _ = configuration;
        return services;
    }
}
