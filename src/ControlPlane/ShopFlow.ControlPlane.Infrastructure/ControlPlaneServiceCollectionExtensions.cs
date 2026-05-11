using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ShopFlow.ControlPlane.Application.Ports;
using ShopFlow.ControlPlane.Infrastructure.Repositories;
using ShopFlow.SharedKernel.Application.Ports;

namespace ShopFlow.ControlPlane.Infrastructure;

/// <summary>
/// Composition-root wiring for the control plane. Per AGENTS.md §11.79 each
/// module exposes a single <c>AddXxxModule</c> entrypoint; the control plane
/// is the "module" responsible for the catalog and channel directory.
/// </summary>
public static class ControlPlaneServiceCollectionExtensions
{
    /// <summary>
    /// Configuration sections consumed by this extension:
    /// <list type="bullet">
    ///   <item><description><c>ControlPlane:ConnectionString</c> — the catalog DB connection (PgBouncer-fronted).</description></item>
    ///   <item><description><c>ControlPlane:TenantTemplate</c> — connection-string template for tenant DBs. The literal token <c>{db}</c> is replaced with the tenant's <c>db_name</c> at projection time.</description></item>
    /// </list>
    /// The migration assembly is hard-pinned to <c>ShopFlow.ControlPlane.Migrations</c>
    /// (the sibling project that owns the catalog schema migrations).
    /// </summary>
    public static IServiceCollection AddControlPlane(
        this IServiceCollection services,
        IConfiguration configuration
    )
    {
        var connectionString =
            configuration["ControlPlane:ConnectionString"]
            ?? throw new InvalidOperationException(
                "ControlPlane:ConnectionString is required (catalog DB connection)."
            );

        var tenantTemplate =
            configuration["ControlPlane:TenantTemplate"]
            ?? throw new InvalidOperationException(
                "ControlPlane:TenantTemplate is required (must contain the literal token '{db}')."
            );

        if (!tenantTemplate.Contains("{db}", StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "ControlPlane:TenantTemplate must contain the literal token '{db}'."
            );
        }

        services.AddDbContext<ControlPlaneDbContext>(options =>
            options.UseNpgsql(
                connectionString,
                npg => npg.MigrationsAssembly("ShopFlow.ControlPlane.Migrations")
            )
        );

        services.AddMemoryCache(options => options.SizeLimit = 1000);

        services.AddScoped<ITenantCatalog>(sp => new TenantCatalog(
            sp.GetRequiredService<ControlPlaneDbContext>(),
            sp.GetRequiredService<IMemoryCache>(),
            tenant => tenantTemplate.Replace("{db}", tenant.DbName, StringComparison.Ordinal)
        ));

        services.AddScoped<IChannelDirectory, ChannelDirectory>();

        return services;
    }
}
