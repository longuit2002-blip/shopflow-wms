using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace ShopFlow.Channel.Infrastructure;

/// <summary>
/// Channel module composition root. Phase-0 stub; concrete registrations
/// (DbContext with the persistent webhook idempotency table, per-channel
/// HMAC adapters, stock-sync engine with coalescing buffer + token bucket
/// + priority queue) land in Phase-2 Sprint-4/5 (W6-7) per
/// docs/plans/2026-04-27-001-feat-shopflow-wms-phase-0-bootstrap-plan.md.
/// </summary>
public static class ChannelServiceCollectionExtensions
{
    public static IServiceCollection AddChannelModule(
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
