using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ShopFlow.Inbound.Application.Ports;
using ShopFlow.Inbound.Infrastructure.Repositories;
using ShopFlow.SharedKernel.Application;
using ShopFlow.SharedKernel.Infrastructure;

namespace ShopFlow.Inbound.Infrastructure;

/// <summary>
/// Composition root for the Inbound module per AGENTS.md §11.79. The Api
/// project's <c>Program.cs</c> calls <c>services.AddShopFlowDefaults(...)</c>
/// first (kernel concerns) and then <c>services.AddInboundModule(configuration)</c>
/// from here. U1 ships the scaffold + DbContext registration + outbox
/// dispatcher hosted service; repositories + handlers land in U2/U3.
/// </summary>
public static class InboundServiceCollectionExtensions
{
    public const string ModuleName = "Inbound";

    public static IServiceCollection AddInboundModule(
        this IServiceCollection services,
        IConfiguration configuration
    )
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddScoped<InboundDbContext>(sp =>
        {
            var ctx = sp.GetRequiredService<IRequestContext>();
            var options = new DbContextOptionsBuilder<InboundDbContext>()
                .UseNpgsql(
                    ctx.DbConnectionString,
                    npg =>
                        npg.MigrationsAssembly(
                            typeof(InboundServiceCollectionExtensions).Assembly.GetName().Name
                        )
                )
                .Options;
            return new InboundDbContext(options);
        });

        services.AddScoped<IPurchaseOrderRepository, PurchaseOrderRepository>();
        services.AddScoped<IUnitOfWork, InboundUnitOfWork>();

        services.AddHostedService<MultiplexedOutboxDispatcher<InboundDbContext>>();

        return services;
    }
}
