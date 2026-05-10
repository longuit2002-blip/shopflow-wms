using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using ShopFlow.Inventory.Application.Handlers;
using ShopFlow.Inventory.Application.Ports;
using ShopFlow.Inventory.Infrastructure.Repositories;
using ShopFlow.SharedKernel.Infrastructure;

namespace ShopFlow.Inventory.Infrastructure;

/// <summary>
/// Inventory module composition root. Registers the DbContext (with the
/// kernel's <see cref="TenancyInterceptor"/> + <see cref="OutboxInterceptor"/>),
/// repository implementations, MediatR handlers from the Application
/// assembly, and a shared <see cref="TimeProvider"/>.
/// </summary>
public static class InventoryServiceCollectionExtensions
{
    public const string ConnectionStringName = "Inventory";

    public static IServiceCollection AddInventoryModule(
        this IServiceCollection services,
        IConfiguration configuration
    )
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.TryAddSingleton(TimeProvider.System);

        var connectionString =
            configuration.GetConnectionString(ConnectionStringName)
            ?? throw new InvalidOperationException(
                $"Connection string '{ConnectionStringName}' is not configured."
            );

        services.AddDbContext<InventoryDbContext>(
            (sp, options) =>
            {
                options.UseNpgsql(
                    connectionString,
                    npg => npg.MigrationsHistoryTable("__ef_migrations_history", "public")
                );

                var tenancy = sp.GetRequiredService<TenancyInterceptor>();
                var outbox = sp.GetRequiredService<OutboxInterceptor>();
                options.AddInterceptors(tenancy, outbox);
            }
        );

        services.AddScoped<IReservationRepository, ReservationRepository>();
        services.AddScoped<IStockItemRepository, StockItemRepository>();
        services.AddScoped<IUnitOfWork, InventoryUnitOfWork>();

        services.AddMediatR(cfg =>
            cfg.RegisterServicesFromAssembly(typeof(ReserveStockHandler).Assembly)
        );

        return services;
    }
}
