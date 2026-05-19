using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ShopFlow.SharedKernel.Application;
using ShopFlow.SharedKernel.Application.Ports;
using ShopFlow.SharedKernel.Domain;

namespace ShopFlow.SharedKernel.Infrastructure.SignalR;

/// <summary>
/// Sprint-7 plan U5 — <see cref="IHubFilter"/> that binds tenancy on
/// SignalR connections. Reads the <c>tenant_slug</c> claim from the
/// connection's <see cref="System.Security.Claims.ClaimsPrincipal"/>,
/// looks the tenant up via <see cref="ITenantCatalog"/>, joins the
/// connection to the <c>"tenant:{slug}"</c> group, and on every hub
/// method invocation opens a DI scope and binds
/// <see cref="RequestContext"/> so any scoped service the method touches
/// resolves against the correct per-tenant DbContext.
/// </summary>
/// <remarks>
/// <para>This is the SignalR mirror of <see cref="TenantRoutingMiddleware"/>
/// — the kernel's default-deny tenancy primitive — adapted for the
/// persistent-connection model. Same rejection codes:</para>
/// <list type="bullet">
///   <item><description>Missing <c>tenant_slug</c> claim → <see cref="HubCallerContext.Abort"/>.</description></item>
///   <item><description>Unknown slug (catalog returns null) → abort.</description></item>
///   <item><description>Slug found but <see cref="TenantInfo.Status"/> != <see cref="TenantStatus.Ready"/> → abort.</description></item>
/// </list>
///
/// <para>The DI scope dance mirrors
/// <see cref="ShopFlow.StockSync.Infrastructure.Persistence.Repositories.CachingSkuFlagRepository"/>
/// (KTD7) and <see cref="Sagas.TenantBindingSagaFilter{T}"/> (K12) — the
/// filter itself is registered scoped, but SignalR's hub-method
/// dispatcher runs on the global hub services, so we open a fresh
/// <see cref="IServiceScopeFactory.CreateAsyncScope"/> per invocation,
/// resolve <see cref="RequestContext"/> inside it, bind, then call
/// <c>next</c>.</para>
///
/// <para>Hub group naming: <c>"tenant:{slug}"</c>. U6 relay consumers
/// publish to this exact key via <c>IHubContext.Clients.Group(...)</c>.
/// Slug (not id) so the group name is human-readable in SignalR
/// diagnostics; tenancy correctness is enforced by the catalog lookup +
/// bound RequestContext, not by the group name itself.</para>
/// </remarks>
public sealed class TenantBindingHubFilter : IHubFilter
{
    /// <summary>
    /// JWT claim key carrying the tenant slug — same as
    /// <see cref="TenantRoutingMiddleware.JwtTenantClaim"/>.
    /// </summary>
    public const string JwtTenantClaim = TenantRoutingMiddleware.JwtTenantClaim;

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ITenantCatalog _tenantCatalog;
    private readonly ILogger<TenantBindingHubFilter> _logger;

    /// <summary>
    /// Production constructor — <see cref="ITenantCatalog"/> is injected
    /// directly for simple callers (singleton catalog impls or scoped
    /// catalogs resolved by SignalR's per-invocation scope). The inner
    /// scope opened in <see cref="OnConnectedAsync"/> /
    /// <see cref="InvokeMethodAsync"/> is for binding <see cref="RequestContext"/>
    /// — the catalog instance crosses scope boundaries safely because
    /// <see cref="ITenantCatalog"/> is a pure read-side cache (see
    /// <c>TenantCatalog</c> in ControlPlane.Infrastructure).
    /// </summary>
    public TenantBindingHubFilter(
        IServiceScopeFactory scopeFactory,
        ITenantCatalog tenantCatalog,
        ILogger<TenantBindingHubFilter> logger
    )
    {
        ArgumentNullException.ThrowIfNull(scopeFactory);
        ArgumentNullException.ThrowIfNull(tenantCatalog);
        ArgumentNullException.ThrowIfNull(logger);

        _scopeFactory = scopeFactory;
        _tenantCatalog = tenantCatalog;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task OnConnectedAsync(
        HubLifetimeContext context,
        Func<HubLifetimeContext, Task> next
    )
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(next);

        var slug = context.Context.User?.FindFirst(JwtTenantClaim)?.Value;
        if (string.IsNullOrWhiteSpace(slug))
        {
            _logger.LogWarning(
                "SignalR connection {ConnectionId} rejected — missing '{Claim}' claim.",
                context.Context.ConnectionId,
                JwtTenantClaim
            );
            context.Context.Abort();
            return;
        }

        var normalized = slug.Trim().ToLowerInvariant();

        // Catalog read happens inside a fresh scope per the U5 plan —
        // mirrors the KTD7 singleton-scope-binding pattern from
        // CachingSkuFlagRepository.WithTenantScopeAsync. We resolve the
        // catalog from the inner scope rather than the constructor-injected
        // instance so a scoped TenantCatalog (the ControlPlane default)
        // binds correctly even when the filter is invoked outside a
        // request scope. RequestContext is NOT bound here — that happens
        // per-method in InvokeMethodAsync.
        TenantInfo? tenant;
        await using (var scope = _scopeFactory.CreateAsyncScope())
        {
            var ct = context.Context.ConnectionAborted;
            var catalog =
                scope.ServiceProvider.GetService<ITenantCatalog>() ?? _tenantCatalog;
            tenant = await catalog
                .LookupBySlugAsync(normalized, ct)
                .ConfigureAwait(false);
        }

        if (tenant is null)
        {
            _logger.LogWarning(
                "SignalR connection {ConnectionId} rejected — unknown tenant slug '{Slug}'.",
                context.Context.ConnectionId,
                normalized
            );
            context.Context.Abort();
            return;
        }

        if (tenant.Status != TenantStatus.Ready)
        {
            _logger.LogWarning(
                "SignalR connection {ConnectionId} rejected — tenant '{Slug}' status is {Status}.",
                context.Context.ConnectionId,
                normalized,
                tenant.Status
            );
            context.Context.Abort();
            return;
        }

        await context
            .Hub.Groups.AddToGroupAsync(
                context.Context.ConnectionId,
                BuildGroupName(normalized),
                context.Context.ConnectionAborted
            )
            .ConfigureAwait(false);

        _logger.LogDebug(
            "SignalR connection {ConnectionId} joined tenant group '{Group}'.",
            context.Context.ConnectionId,
            BuildGroupName(normalized)
        );

        await next(context).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async ValueTask<object?> InvokeMethodAsync(
        HubInvocationContext invocationContext,
        Func<HubInvocationContext, ValueTask<object?>> next
    )
    {
        ArgumentNullException.ThrowIfNull(invocationContext);
        ArgumentNullException.ThrowIfNull(next);

        var slug = invocationContext.Context.User?.FindFirst(JwtTenantClaim)?.Value;
        if (string.IsNullOrWhiteSpace(slug))
        {
            // OnConnectedAsync should have already rejected this, but
            // defend against direct hub-method-invocation paths in tests.
            invocationContext.Context.Abort();
            return null;
        }

        var normalized = slug.Trim().ToLowerInvariant();
        var ct = invocationContext.Context.ConnectionAborted;

        await using var scope = _scopeFactory.CreateAsyncScope();
        var catalog =
            scope.ServiceProvider.GetService<ITenantCatalog>() ?? _tenantCatalog;
        var tenant = await catalog
            .LookupBySlugAsync(normalized, ct)
            .ConfigureAwait(false);
        if (tenant is null || tenant.Status != TenantStatus.Ready)
        {
            invocationContext.Context.Abort();
            return null;
        }

        var requestContext = scope.ServiceProvider.GetRequiredService<RequestContext>();
        var correlationId = Guid.NewGuid().ToString("N");
        requestContext.Bind(tenant, correlationId, userId: null);

        return await next(invocationContext).ConfigureAwait(false);
    }

    /// <summary>
    /// Compute the group name for a given tenant slug. Exposed so relay
    /// consumers (U6) can call into the hub context with the same key.
    /// </summary>
    public static string BuildGroupName(string slug)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(slug);
        return $"tenant:{slug.Trim().ToLowerInvariant()}";
    }
}
