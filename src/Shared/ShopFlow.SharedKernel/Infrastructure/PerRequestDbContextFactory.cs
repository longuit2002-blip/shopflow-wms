using Microsoft.EntityFrameworkCore;
using ShopFlow.SharedKernel.Application;

namespace ShopFlow.SharedKernel.Infrastructure;

/// <summary>
/// EF Core <see cref="IDbContextFactory{TContext}"/> implementation that
/// reads the connection string from the ambient <see cref="IRequestContext"/>
/// at every <see cref="CreateDbContext"/> call. This is the new tenant-
/// correctness primitive under ADR-0003 (DB-per-tenant): each request /
/// dispatched message binds <c>IRequestContext.DbConnectionString</c> via
/// <see cref="TenantRoutingMiddleware"/> (or its consumer equivalent), and
/// every DbContext constructed in that scope hits the right tenant DB.
/// </summary>
/// <remarks>
/// Registered as <c>Scoped</c> so the underlying <see cref="IRequestContext"/>
/// resolution is per-request. EF Core's built-in pooled
/// <see cref="IDbContextFactory{TContext}"/> is unsuitable here because it
/// captures the connection string at registration time; per-tenant routing
/// requires per-call resolution.
/// </remarks>
public sealed class PerRequestDbContextFactory<TContext> : IDbContextFactory<TContext>
    where TContext : DbContext
{
    private readonly IRequestContext _requestContext;

    public PerRequestDbContextFactory(IRequestContext requestContext)
    {
        _requestContext = requestContext;
    }

    public TContext CreateDbContext()
    {
        var connectionString = _requestContext.DbConnectionString;
        // Use EF Core's default Npgsql migrations-history table name
        // (__EFMigrationsHistory). shopflow-migrate (the production
        // migration runner) also leaves this at the default, so every
        // consumer of a tenant DB resolves to the same history table.
        var builder = new DbContextOptionsBuilder<TContext>().UseNpgsql(connectionString);

        var ctx =
            Activator.CreateInstance(typeof(TContext), builder.Options) as TContext
            ?? throw new InvalidOperationException(
                $"DbContext type {typeof(TContext).FullName} must expose a public constructor "
                    + "taking DbContextOptions<TContext>."
            );

        return ctx;
    }
}
