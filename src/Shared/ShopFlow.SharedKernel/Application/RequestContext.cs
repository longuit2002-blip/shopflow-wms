using ShopFlow.SharedKernel.Application.Ports;

namespace ShopFlow.SharedKernel.Application;

/// <summary>
/// Default <see cref="IRequestContext"/> implementation populated by
/// <see cref="Infrastructure.TenantRoutingMiddleware"/> at the API boundary.
/// Reading any tenant-scoped property before <see cref="Bind(TenantInfo, string, System.Nullable{System.Guid})"/>
/// has been called throws — the failure mode is loud rather than a silent
/// zero-tenant query.
/// </summary>
public sealed class RequestContext : IRequestContext
{
    private TenantInfo? _tenant;
    private string _correlationId = string.Empty;

    public Guid TenantId => Tenant.Id;

    public string TenantSlug => Tenant.Slug;

    public string DbConnectionString => Tenant.DbConnectionString;

    public string CorrelationId =>
        _correlationId.Length > 0
            ? _correlationId
            : throw new InvalidOperationException(
                "IRequestContext.CorrelationId accessed before the request boundary populated it. "
                    + "Ensure the tenant-resolution middleware runs before any handler invocation."
            );

    public Guid? UserId { get; private set; }

    private TenantInfo Tenant =>
        _tenant
        ?? throw new InvalidOperationException(
            "IRequestContext tenant scope accessed before the request boundary populated it. "
                + "Ensure TenantRoutingMiddleware runs before any handler invocation."
        );

    /// <summary>
    /// Binds the request scope to a tenant. Called by the routing middleware
    /// after the catalog lookup succeeds. The dispatcher / background-worker
    /// equivalents call this from a message-header-derived <see cref="TenantInfo"/>.
    /// </summary>
    public void Bind(TenantInfo tenant, string correlationId, Guid? userId)
    {
        ArgumentNullException.ThrowIfNull(tenant);
        ArgumentNullException.ThrowIfNull(correlationId);

        _tenant = tenant;
        _correlationId = correlationId;
        UserId = userId;
    }
}
