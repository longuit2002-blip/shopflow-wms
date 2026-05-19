using System.Diagnostics;
using MassTransit;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using ShopFlow.Contracts.Outbound;
using ShopFlow.SharedKernel.Application.Ports;

namespace ShopFlow.SharedKernel.Infrastructure.SignalR;

/// <summary>
/// Sprint-7 plan U6 — relay consumer that bridges the
/// <see cref="SagaTransitionedV1"/> integration event onto the
/// tenant-scoped SignalR group as a <c>"saga_transitioned"</c> hub event.
/// </summary>
/// <remarks>
/// <para>Mirror of <see cref="StockChangedRelayConsumer"/>; see that class's
/// remarks for the single-hub-host architecture decision and the
/// catalog-driven tenant resolution rationale. Same failure-mode policy:
/// unknown tenant id → log + return cleanly, hub send throws → bubble.</para>
///
/// <para>The frontend Orders detail surface (Sprint-7 U7+) subscribes to the
/// <c>"saga_transitioned"</c> event and renders each
/// <see cref="SagaTransitionedPayload.EventType"/> in a small monospace
/// transition log. <see cref="SagaTransitionedPayload.OrderId"/> lets the
/// hook filter to the currently-viewed order.</para>
/// </remarks>
public sealed class SagaTransitionedRelayConsumer : IConsumer<SagaTransitionedV1>
{
    /// <summary>
    /// Hub event name surfaced to the SignalR client. The frontend
    /// <c>useSignalR</c> hook (Sprint-7 U7) binds to this exact string.
    /// </summary>
    public const string HubEventName = "saga_transitioned";

    private readonly IHubContext<TenantHub> _hub;
    private readonly ITenantCatalog _catalog;
    private readonly ILogger<SagaTransitionedRelayConsumer> _logger;

    public SagaTransitionedRelayConsumer(
        IHubContext<TenantHub> hub,
        ITenantCatalog catalog,
        ILogger<SagaTransitionedRelayConsumer> logger
    )
    {
        ArgumentNullException.ThrowIfNull(hub);
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(logger);

        _hub = hub;
        _catalog = catalog;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task Consume(ConsumeContext<SagaTransitionedV1> context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var msg = context.Message;
        var ct = context.CancellationToken;

        var tenant = await _catalog.LookupByIdAsync(msg.TenantId, ct).ConfigureAwait(false);
        if (tenant is null)
        {
            _logger.LogWarning(
                "SagaTransitionedRelay: unknown tenant id {TenantId} for order {OrderId} — dropping (no SignalR group to target).",
                msg.TenantId,
                msg.OrderId
            );
            return;
        }

        // AGENTS.md §6.43 — propagate W3C TraceContext via Activity.Current.
        // Prefer the producer's correlation_id (carried in the integration
        // event envelope) over a freshly minted one so the hub payload's
        // trace ties back to the saga transition's audit row.
        var correlationId =
            !string.IsNullOrWhiteSpace(msg.CorrelationId)
                ? msg.CorrelationId
                : Activity.Current?.Id ?? Guid.NewGuid().ToString("N");

        var payload = new SagaTransitionedPayload(
            TenantId: msg.TenantId,
            OrderId: msg.OrderId,
            FromState: msg.FromState,
            ToState: msg.ToState,
            OccurredAt: msg.OccurredAt,
            EventType: msg.EventType,
            CorrelationId: correlationId
        );

        var groupName = TenantBindingHubFilter.BuildGroupName(tenant.Slug);
        await _hub
            .Clients.Group(groupName)
            .SendAsync(HubEventName, payload, ct)
            .ConfigureAwait(false);

        _logger.LogDebug(
            "SagaTransitionedRelay: pushed {EventName} to {Group} (order={OrderId}, {FromState}->{ToState}).",
            HubEventName,
            groupName,
            msg.OrderId,
            msg.FromState,
            msg.ToState
        );
    }
}
