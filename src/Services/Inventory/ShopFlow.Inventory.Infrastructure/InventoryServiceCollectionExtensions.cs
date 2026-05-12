using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ShopFlow.Inventory.Application;
using ShopFlow.Inventory.Application.Ports;
using ShopFlow.Inventory.Infrastructure.Repositories;
using ShopFlow.Inventory.Infrastructure.Workers;
using ShopFlow.SharedKernel.Application;
using ShopFlow.SharedKernel.Infrastructure;

namespace ShopFlow.Inventory.Infrastructure;

/// <summary>
/// Composition root for the Inventory module per AGENTS.md §11.79. The
/// Api project's <c>Program.cs</c> calls
/// <c>services.AddShopFlowDefaults(...)</c> first (kernel concerns) and
/// then <c>services.AddInventoryModule(configuration)</c> from here.
/// </summary>
/// <remarks>
/// <para>Wires:</para>
/// <list type="bullet">
///   <item><description><see cref="InventoryDbContext"/> via
///   <see cref="IDbContextFactory{TContext}"/> — per-request connection
///   string from <see cref="IRequestContext.DbConnectionString"/>
///   (AGENTS.md §3.17).</description></item>
///   <item><description><see cref="IReservationRepository"/>,
///   <see cref="IStockItemRepository"/>, <see cref="IUnitOfWork"/>
///   skeletons.</description></item>
///   <item><description><see cref="ReservationExpiryWorker"/> as a
///   hosted service — the body is Sprint-1-redux.</description></item>
/// </list>
/// </remarks>
public static class InventoryServiceCollectionExtensions
{
    public const string ModuleName = "Inventory";

    public static IServiceCollection AddInventoryModule(
        this IServiceCollection services,
        IConfiguration configuration
    )
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddScoped<InventoryDbContext>(sp =>
        {
            var ctx = sp.GetRequiredService<IRequestContext>();
            var options = new DbContextOptionsBuilder<InventoryDbContext>()
                .UseNpgsql(
                    ctx.DbConnectionString,
                    npg =>
                        npg.MigrationsAssembly(
                            typeof(InventoryServiceCollectionExtensions).Assembly.GetName().Name
                        )
                )
                .Options;
            return new InventoryDbContext(options);
        });

        services.AddScoped<IStockItemRepository, StockItemRepository>();
        services.AddScoped<IReservationRepository, ReservationRepository>();
        services.AddScoped<IUnitOfWork, InventoryUnitOfWork>();

        services
            .AddOptions<InventoryOptions>()
            .Bind(configuration.GetSection(InventoryOptions.SectionName));

        services.AddHostedService<ReservationExpiryWorker>();

        services.TryAddSingleton<TimeProvider>(TimeProvider.System);

        return services;
    }
}

file static class ServiceCollectionExtensions
{
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
