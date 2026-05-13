using MassTransit;
using Microsoft.Extensions.Logging;
using ShopFlow.SharedKernel.Application;
using ShopFlow.SharedKernel.Application.Ports;

namespace ShopFlow.Outbound.Infrastructure.Sagas;

/// <summary>
/// Sprint-3-redux U4 K12 (primary path) — MassTransit consume filter that
/// reads the <c>tenant_id</c> header off the message envelope, resolves
/// the tenant via <see cref="ITenantCatalog"/>, and binds the scoped
/// <see cref="RequestContext"/> BEFORE the saga repository's DbContext
/// resolution runs. That way the <see cref="OutboundDbContext"/>
/// registered in <c>AddOutboundModule</c> (which reads
/// <see cref="IRequestContext.DbConnectionString"/> at construction) picks
/// the correct per-tenant Postgres database.
/// </summary>
/// <remarks>
/// <para>The pattern mirrors Sprint-2-redux's
/// <c>InboundConfirmedConsumer</c>'s in-body header read + RequestContext
/// binding, with the difference that filters run BEFORE the consumer (or
/// saga) body — so the saga repository's <c>ExistingDbContext{T}()</c>
/// resolution can see the bound tenant when it pulls
/// <see cref="OutboundDbContext"/> from the message's DI scope.</para>
///
/// <para>This filter is registered on the saga's receive endpoint via
/// <c>cfg.UseConsumeFilter(typeof(TenantBindingSagaFilter&lt;&gt;),
/// context)</c> — the open-generic registration means every typed saga
/// event (<see cref="ShopFlow.Contracts.Outbound.OrderPlacedV1"/>,
/// <see cref="ShopFlow.Contracts.Inventory.StockReservedV1"/>, ...) flows
/// through it on the way to the saga.</para>
///
/// <para>Failure mode: if the <c>tenant_id</c> header is missing or the
/// tenant lookup returns null, the filter throws — MassTransit moves the
/// message to the DLQ. There is no retry path because a missing tenant
/// header is a routing fault, not a transient failure.</para>
/// </remarks>
public sealed class TenantBindingSagaFilter<T> : IFilter<ConsumeContext<T>>
    where T : class
{
    /// <summary>
    /// Header key that MassTransit envelopes use to carry the tenant id.
    /// Senders (the dispatcher in <c>OutboxDispatcher</c>) set this
    /// header on every outbound publish; the filter reads it on every
    /// inbound consume.
    /// </summary>
    public const string TenantIdHeader = "tenant_id";

    private readonly ITenantCatalog _catalog;
    private readonly RequestContext _requestContext;
    private readonly ILogger<TenantBindingSagaFilter<T>> _logger;

    public TenantBindingSagaFilter(
        ITenantCatalog catalog,
        RequestContext requestContext,
        ILogger<TenantBindingSagaFilter<T>> logger
    )
    {
        _catalog = catalog;
        _requestContext = requestContext;
        _logger = logger;
    }

    public async Task Send(ConsumeContext<T> context, IPipe<ConsumeContext<T>> next)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(next);

        var tenantHeader = context.Headers.Get<string>(TenantIdHeader);
        if (string.IsNullOrWhiteSpace(tenantHeader))
        {
            // Defensive — the dispatcher MUST stamp this header. Missing
            // here means the envelope was published via a non-dispatcher
            // path (e.g., test code that didn't set headers); drop into
            // the DLQ rather than silently bind to the wrong tenant.
            throw new InvalidOperationException(
                $"Saga consume context for {typeof(T).Name} is missing the '{TenantIdHeader}' header. "
                    + "Every cross-module envelope must carry the tenant id; misrouting would write to the wrong tenant DB."
            );
        }

        if (!Guid.TryParse(tenantHeader, out var tenantId))
        {
            throw new InvalidOperationException(
                $"Saga consume context for {typeof(T).Name} carried an unparseable '{TenantIdHeader}' header: '{tenantHeader}'."
            );
        }

        var tenant = await _catalog
            .LookupByIdAsync(tenantId, context.CancellationToken)
            .ConfigureAwait(false);
        if (tenant is null)
        {
            throw new InvalidOperationException(
                $"Saga consume context for {typeof(T).Name} carried tenant id '{tenantId}' which the catalog could not resolve."
            );
        }

        var correlationId = context.CorrelationId?.ToString() ?? Guid.NewGuid().ToString("N");
        _requestContext.Bind(tenant, correlationId, userId: null);

        _logger.LogDebug(
            "TenantBindingSagaFilter bound tenant {TenantSlug} for {MessageType} (correlation {CorrelationId}).",
            tenant.Slug,
            typeof(T).Name,
            correlationId
        );

        await next.Send(context).ConfigureAwait(false);
    }

    public void Probe(ProbeContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        context.CreateFilterScope("tenant-binding-saga");
    }
}
