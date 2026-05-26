using MassTransit;
using MassTransit.EntityFrameworkCoreIntegration;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ShopFlow.Outbound.Application.Sagas;
using ShopFlow.SharedKernel.Application;
using ShopFlow.SharedKernel.Application.Ports;

namespace ShopFlow.Outbound.Infrastructure.Sagas;

/// <summary>
/// Sprint-3-redux U4 K12 (fallback path) — custom
/// <see cref="ISagaDbContextFactory{TSaga}"/> for the
/// <see cref="FulfillmentSagaState"/> that resolves the tenant from the
/// <see cref="ConsumeContext"/> envelope header at message-receive time
/// instead of from a startup-time captured connection string.
/// </summary>
/// <remarks>
/// <para>This is the documented fallback path per the plan. Sprint-3-redux
/// U4 prefers <see cref="TenantBindingSagaFilter{T}"/> because:
/// (a) the filter composes cleanly with the existing
/// <c>OutboundDbContext</c> Scoped registration in
/// <c>AddOutboundModule</c> — which already reads
/// <see cref="IRequestContext.DbConnectionString"/>;
/// (b) the filter sits BEFORE the saga repository in the consume
/// pipeline, so MT's <c>ExistingDbContext{T}()</c> binding picks up the
/// just-bound RequestContext through standard DI.</para>
///
/// <para>This factory remains here for the K12 fallback scenario: if
/// the filter path turns out to mis-order against MT's saga-repo
/// internals on Postgres, switch the saga repo's <c>.DatabaseFactory(...)</c>
/// to use this factory directly (it does the binding inside
/// <see cref="CreateScoped{T}"/>). Both paths produce the same outcome:
/// every saga write/read hits the right tenant DB.</para>
/// </remarks>
public sealed class TenantAwareSagaDbContextFactory : ISagaDbContextFactory<FulfillmentSagaState>
{
    private readonly IServiceProvider _rootServiceProvider;

    public TenantAwareSagaDbContextFactory(IServiceProvider rootServiceProvider)
    {
        _rootServiceProvider = rootServiceProvider;
    }

    /// <summary>
    /// Standalone DbContext factory — used by MT for non-message-scoped
    /// paths (e.g., audit endpoint or migration runs). Since there is no
    /// <see cref="ConsumeContext"/> here, this path is intentionally
    /// unsupported in the tenant-routing world: each tenant DB has its
    /// own connection string, so a standalone factory cannot pick the
    /// right one without ambient context. Returning a freshly-constructed
    /// context with NO connection string would fail loudly on the first
    /// query — better than silent wrong-DB writes.
    /// </summary>
    public DbContext Create()
    {
        throw new InvalidOperationException(
            "Standalone OutboundDbContext creation is not supported under per-tenant routing. "
                + "Use Create(ConsumeContext) with a tenant_id header on the envelope."
        );
    }

    /// <summary>
    /// Message-scoped factory — invoked by MT's saga repository inside
    /// the receive pipeline. Reads the envelope's tenant_id header, looks
    /// up the tenant via <see cref="ITenantCatalog"/>, binds the scoped
    /// <see cref="RequestContext"/>, then resolves the
    /// <see cref="OutboundDbContext"/> from the message's DI scope — at
    /// which point the per-request DbContext factory picks the correct
    /// per-tenant connection string.
    /// </summary>
    public DbContext CreateScoped<T>(ConsumeContext<T> context)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(context);

        // The consume scope is what the saga repository operates inside
        // — pull its IServiceProvider and bind RequestContext + resolve
        // OutboundDbContext from there.
        var serviceProvider =
            context.GetPayload<IServiceProvider>()
            ?? throw new InvalidOperationException(
                "ConsumeContext does not carry an IServiceProvider payload. "
                    + "Ensure MassTransit is configured with the DI scope provider."
            );

        var tenantHeader = context.Headers.Get<string>(TenantBindingSagaFilter<T>.TenantIdHeader);
        if (
            string.IsNullOrWhiteSpace(tenantHeader)
            || !Guid.TryParse(tenantHeader, out var tenantId)
        )
        {
            throw new InvalidOperationException(
                $"Cannot create per-tenant OutboundDbContext: missing or invalid '{TenantBindingSagaFilter<T>.TenantIdHeader}' header."
            );
        }

        var catalog = serviceProvider.GetRequiredService<ITenantCatalog>();
        var tenant =
            catalog.LookupByIdAsync(tenantId, context.CancellationToken).GetAwaiter().GetResult()
            ?? throw new InvalidOperationException($"Tenant '{tenantId}' not found in catalog.");

        var requestContext = serviceProvider.GetRequiredService<RequestContext>();
        var correlationId = context.CorrelationId?.ToString() ?? Guid.NewGuid().ToString("N");
        requestContext.Bind(tenant, correlationId, userId: null);

        return serviceProvider.GetRequiredService<OutboundDbContext>();
    }

    /// <summary>
    /// Release the DbContext after the saga repository operation
    /// completes. The Scoped lifetime owns disposal — calling Dispose
    /// here would short-circuit the scope's own cleanup.
    /// </summary>
    public ValueTask ReleaseAsync(DbContext context) => ValueTask.CompletedTask;
}
