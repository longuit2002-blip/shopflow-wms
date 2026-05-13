using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ShopFlow.SharedKernel.Application;
using ShopFlow.SharedKernel.Infrastructure;

namespace ShopFlow.Outbound.Infrastructure;

/// <summary>
/// Composition root for the Outbound module per AGENTS.md §11.79. The
/// Api project's <c>Program.cs</c> calls
/// <c>services.AddShopFlowDefaults(...)</c> first (kernel concerns) and
/// then <c>services.AddOutboundModule(configuration)</c> from here. U1
/// ships the scaffold + DbContext registration + outbox dispatcher
/// hosted service; repositories + saga + handlers land in U2/U4/U5/U6.
/// </summary>
public static class OutboundServiceCollectionExtensions
{
    public const string ModuleName = "Outbound";

    public static IServiceCollection AddOutboundModule(
        this IServiceCollection services,
        IConfiguration configuration
    )
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddScoped<OutboundDbContext>(sp =>
        {
            var ctx = sp.GetRequiredService<IRequestContext>();
            var options = new DbContextOptionsBuilder<OutboundDbContext>()
                .UseNpgsql(
                    ctx.DbConnectionString,
                    npg =>
                        npg.MigrationsAssembly(
                            typeof(OutboundServiceCollectionExtensions).Assembly.GetName().Name
                        )
                )
                .Options;
            return new OutboundDbContext(options);
        });

        // U2+ register IOrderRepository / IPickWaveRepository / IUnitOfWork /
        // IOutboundOutbox here. U4 registers the FulfillmentSaga state machine
        // + MassTransit EF saga repository against the saga_state table (the
        // EntityFrameworkRepository binding lives here once the saga class
        // exists). U5 registers the IPickQueue singleton +
        // PickWaveGeneratorService hosted service. U6 registers the Polly
        // pipeline + IMockShippingProvider singleton.

        services.TryAddSingleton<TimeProvider>(TimeProvider.System);

        services.AddHostedService<MultiplexedOutboxDispatcher<OutboundDbContext>>();

        return services;
    }
}

file static class ServiceCollectionExtensions
{
    /// <summary>
    /// Mirrors the Inbound / Inventory pattern: only register the instance
    /// if no existing registration is present. The OOTB
    /// <c>Microsoft.Extensions.DependencyInjection.Extensions.TryAddSingleton</c>
    /// takes an implementation type, not an instance.
    /// </summary>
    public static void TryAddSingleton<TService>(
        this IServiceCollection services,
        TService instance
    )
        where TService : class
    {
        if (!services.Any(d => d.ServiceType == typeof(TService)))
        {
            services.AddSingleton(instance);
        }
    }
}
