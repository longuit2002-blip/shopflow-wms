using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace ShopFlow.Outbound.Infrastructure;

/// <summary>
/// Outbound module composition root. Phase-0 stub; concrete registrations
/// (DbContext, MassTransit fulfillment saga state, MediatR handlers for
/// Reserve / Pick / Pack / Ship transitions with compensation) land in
/// Phase-1 Sprint-3 (W5) per
/// docs/plans/2026-04-27-001-feat-shopflow-wms-phase-0-bootstrap-plan.md.
/// </summary>
public static class OutboundServiceCollectionExtensions
{
    public static IServiceCollection AddOutboundModule(
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
