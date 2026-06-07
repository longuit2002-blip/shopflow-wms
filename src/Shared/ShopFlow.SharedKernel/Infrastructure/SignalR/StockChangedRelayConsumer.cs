using System.Diagnostics;
using MassTransit;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using ShopFlow.Contracts.Inventory;
using ShopFlow.SharedKernel.Application.Ports;

namespace ShopFlow.SharedKernel.Infrastructure.SignalR;

/// <summary>
/// Sprint-7 plan U6 — relay consumer that bridges the
/// <see cref="StockLevelChangedV1"/> integration event onto the
/// tenant-scoped SignalR group as a <c>"stock_changed"</c> hub event.
/// </summary>
/// <remarks>
/// <para>Per the Sprint-7 doc-review SINGLE-HUB-HOST decision, this consumer
/// is registered ONLY on <c>Outbound.Api</c> (via the
/// <c>AddOutboundModule</c> composition extension's MassTransit block).
/// Registering it in <c>AddShopFlowDefaults</c> would cause every module
/// process to subscribe to the integration event, and RabbitMQ's
/// competing-consumer semantics would deliver each event to only one
/// process — while the connected client lives on whichever process the
/// Gateway routes <c>/hub</c> to. Single-host avoids that race.</para>
///
/// <para>Tenant resolution happens here (not in the producer) because the
/// integration event carries only the <c>TenantId</c> (UUID); the SignalR
/// group naming convention is <c>"tenant:{slug}"</c>
/// (see <see cref="TenantBindingHubFilter.BuildGroupName"/>). The slug comes
/// from the control-plane <see cref="ITenantCatalog"/> which is a cached
/// singleton — no DI scope binding is needed here because the consumer
/// does not write to a per-tenant DbContext.</para>
///
/// <para>Failure modes:</para>
/// <list type="bullet">
///   <item><description>Tenant id unknown to the catalog → log warning + return cleanly.
///     This is a data-shape problem, not an infrastructure failure; DLQ-ing
///     would block all subsequent events on the same queue while a
///     decommissioned tenant's residual messages drain.</description></item>
///   <item><description><see cref="IHubContext{THub}.Clients"/> <c>.SendAsync</c> throws →
///     bubble up. MassTransit's built-in retry policy handles transient SignalR
///     transport failures.</description></item>
/// </list>
/// </remarks>
public sealed class StockChangedRelayConsumer : IConsumer<StockLevelChangedV1>
{
    /// <summary>
    /// Hub event name surfaced to the SignalR client. The frontend
    /// <c>useSignalR</c> hook (Sprint-7 U7) binds to this exact string.
    /// </summary>
    public const string HubEventName = "stock_changed";

    private readonly IHubContext<TenantHub> _hub;
    private readonly ITenantCatalog _catalog;
    private readonly ILogger<StockChangedRelayConsumer> _logger;

    public StockChangedRelayConsumer(
        IHubContext<TenantHub> hub,
        ITenantCatalog catalog,
        ILogger<StockChangedRelayConsumer> logger
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
    public async Task Consume(ConsumeContext<StockLevelChangedV1> context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var msg = context.Message;
        var ct = context.CancellationToken;

        var tenant = await _catalog.LookupByIdAsync(msg.TenantId, ct).ConfigureAwait(false);
        if (tenant is null)
        {
            _logger.LogWarning(
                "StockChangedRelay: unknown tenant id {TenantId} for SKU {Sku} — dropping (no SignalR group to target).",
                msg.TenantId,
                msg.Sku
            );
            return;
        }

        // AGENTS.md §6.43 — propagate W3C TraceContext via Activity.Current.
        // The dispatcher restores trace state when it consumes the outbox row,
        // so Activity.Current here is the same trace as the producer.
        var correlationId = Activity.Current?.Id ?? Guid.NewGuid().ToString("N");

        var payload = new StockChangedPayload(
            TenantId: msg.TenantId,
            Sku: msg.Sku,
            AvailableToSell: msg.AvailableToSell,
            OccurredAt: msg.OccurredAt,
            CorrelationId: correlationId
        );

        var groupName = TenantBindingHubFilter.BuildGroupName(tenant.Slug);
        await _hub
            .Clients.Group(groupName)
            .SendAsync(HubEventName, payload, ct)
            .ConfigureAwait(false);

        _logger.LogDebug(
            "StockChangedRelay: pushed {EventName} to {Group} (sku={Sku}, available={Available}).",
            HubEventName,
            groupName,
            msg.Sku,
            msg.AvailableToSell
        );
    }
}
