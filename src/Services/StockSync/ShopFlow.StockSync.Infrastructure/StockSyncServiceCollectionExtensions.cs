using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ShopFlow.Contracts.Inventory;
using ShopFlow.SharedKernel.Application;
using ShopFlow.SharedKernel.Infrastructure;
using ShopFlow.StockSync.Application.Coalescing;
using ShopFlow.StockSync.Application.Dispatch;
using ShopFlow.StockSync.Application.Options;
using ShopFlow.StockSync.Application.Ports;
using ShopFlow.StockSync.Infrastructure.Background;
using ShopFlow.StockSync.Infrastructure.Breaker;
using ShopFlow.StockSync.Infrastructure.Dispatch;
using ShopFlow.StockSync.Infrastructure.Persistence.Repositories;
using ShopFlow.StockSync.Infrastructure.Pipeline;
using ShopFlow.StockSync.Infrastructure.RateLimit;

namespace ShopFlow.StockSync.Infrastructure;

/// <summary>
/// StockSync module composition root per Sprint-5 plan U8. Modules call this
/// from <c>Program.cs</c> after <c>AddShopFlowDefaults</c> +
/// <c>AddControlPlane</c>; the Sprint-4 <c>AddChannelModule</c> is the gold
/// reference for the shape.
/// </summary>
/// <remarks>
/// <para>Wires:</para>
/// <list type="bullet">
///   <item><description><see cref="StockSyncOptions"/> bound from
///   configuration (<c>StockSync</c> section).</description></item>
///   <item><description><see cref="StockSyncDbContext"/> via
///   <see cref="IDbContextFactory{TContext}"/> — per-request connection
///   string from <see cref="IRequestContext.DbConnectionString"/>
///   (K12 pattern, same shape Sprint-4 Channel uses).</description></item>
///   <item><description><see cref="ISkuFlagRepository"/> singleton-cached
///   wrapper around the scoped DB-backed inner (U7).</description></item>
///   <item><description><see cref="ICoalescingBuffer"/>,
///   <see cref="IPerTenantQueue"/>,
///   <see cref="TenantChannelBucketRegistry"/>,
///   <see cref="TenantChannelBreakerRegistry"/>,
///   <see cref="PushPipelineFactory"/> as process singletons — one shared
///   set of in-memory state per host.</description></item>
///   <item><description><see cref="CoalesceFlushService"/> +
///   <see cref="PerTenantDispatcherService"/> as
///   <see cref="Microsoft.Extensions.Hosting.IHostedService"/>.</description></item>
///   <item><description><see cref="MultiplexedOutboxDispatcher{TContext}"/>
///   for the StockSync DbContext — same pattern Sprint-4 Channel uses for
///   its own outbox.</description></item>
///   <item><description><see cref="StockLevelChangedV1"/> publish route via
///   <c>AddOutboxRoute</c> — Sprint-5 doesn't emit cross-module commands
///   yet, but the placeholder keeps the route registry shape consistent
///   for Phase-3 work.</description></item>
/// </list>
/// </remarks>
public static class StockSyncServiceCollectionExtensions
{
    public const string ModuleName = "StockSync";

    public static IServiceCollection AddStockSyncModule(
        this IServiceCollection services,
        IConfiguration configuration
    )
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        // ---- StockSyncOptions --------------------------------------------
        services
            .AddOptions<StockSyncOptions>()
            .Bind(configuration.GetSection(StockSyncOptions.SectionName));

        // ---- StockSyncDbContext via IDbContextFactory (per-request scope)
        // K12 pattern — the factory builds a fresh DbContext per call bound
        // to IRequestContext.DbConnectionString. AddScoped<StockSyncDbContext>
        // makes the controller / repo seam identical to Sprint-4 Channel.
        services.AddDbContextFactory<StockSyncDbContext>(
            (sp, options) =>
            {
                var requestContext = sp.GetRequiredService<IRequestContext>();
                options.UseNpgsql(
                    requestContext.DbConnectionString,
                    npg => npg.MigrationsAssembly("ShopFlow.StockSync.Infrastructure")
                );
            }
        );

        services.AddScoped<StockSyncDbContext>(sp =>
        {
            var factory = sp.GetRequiredService<IDbContextFactory<StockSyncDbContext>>();
            return factory.CreateDbContext();
        });

        // ---- SkuFlag (U7) ------------------------------------------------
        // The DB-backed inner is scoped (DbContext-bound); the caching
        // wrapper is singleton (5-min TTL, 10k-entry soft cap). The wrapper
        // opens its own service scope on misses + writes so the inner's
        // scope contract holds.
        services.AddScoped<SkuFlagRepository>();
        services.AddSingleton<ISkuFlagRepository, CachingSkuFlagRepository>();

        // ---- Push log (U5) -----------------------------------------------
        services.AddScoped<IPushLogRepository, PushLogRepository>();

        // ---- Channel lookup (U3 port, U8 impl) ---------------------------
        // Singleton — reads StockSync:ActiveChannels once at startup and
        // returns it for every tenant. Phase-3 swaps to a per-tenant query.
        services.AddSingleton<IChannelLookupPort, ChannelLookupPort>();

        // ---- Coalescing + dispatch (U3 / U4 / U5) ------------------------
        services.AddSingleton<ICoalescingBuffer, CoalescingBuffer>();
        services.AddSingleton<IPerTenantQueue, PerTenantQueue>();
        services.AddSingleton<TenantChannelBucketRegistry>();
        services.AddSingleton<PushPipelineFactory>();
        services.AddSingleton<TenantChannelBreakerRegistry>();

        // ---- HostedServices ---------------------------------------------
        services.AddHostedService<CoalesceFlushService>();
        services.AddHostedService<PerTenantDispatcherService>();

        // ---- K13 outbox routes (Sprint-4 U4 pattern) ---------------------
        // StockLevelChangedV1 is the input contract this module consumes via
        // StockLevelChangedConsumer; routing it as Publish keeps the
        // OutboxRouteRegistry shape consistent if the StockSync module ever
        // re-emits it (e.g., Phase-3 enrichment fanout). The Inventory
        // module is the real producer.
        services.AddOutboxRoute<StockLevelChangedV1>(SendKind.Publish);

        // ---- Outbox dispatcher (Sprint-1-redux pattern, StockSync module)
        services.AddHostedService<MultiplexedOutboxDispatcher<StockSyncDbContext>>();

        // ---- TimeProvider (Sprint-1-redux pattern) -----------------------
        services.TryAddSingleton<TimeProvider>(TimeProvider.System);

        return services;
    }
}

file static class ServiceCollectionExtensions
{
    /// <summary>
    /// Mirrors the Inventory / Inbound / Outbound pattern: only register the
    /// instance if no existing registration is present. The OOTB
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
