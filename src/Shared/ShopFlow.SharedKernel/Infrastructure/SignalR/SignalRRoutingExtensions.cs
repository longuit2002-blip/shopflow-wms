using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.SignalR;

namespace ShopFlow.SharedKernel.Infrastructure.SignalR;

/// <summary>
/// Sprint-7 plan U5 — endpoint-routing extension that maps
/// <see cref="TenantHub"/> at the single canonical <c>/hub</c> path.
/// </summary>
/// <remarks>
/// <para>Per the Sprint-7 doc-review SINGLE-HUB-HOST decision, only
/// <c>Outbound.Api</c> calls <see cref="MapShopFlowHubs"/> on its endpoint
/// route builder. Inventory.Api / StockSync.Api / Channel.Api / Inbound.Api
/// intentionally do NOT call this. The Gateway forwards <c>/hub</c> to
/// the Outbound cluster (see <c>src/ApiGateway/ShopFlow.Gateway/appsettings.json</c>).
/// Rationale: avoids the RabbitMQ competing-consumer trap on the eventual
/// W6 process split — if every module process subscribed to the hub
/// events the connected client would only see whichever process happened
/// to consume each event.</para>
///
/// <para>Auth.Api is also excluded because its <c>Program.cs</c>
/// intentionally skips <c>AddShopFlowDefaults</c> (banner: "NOT wired
/// through AddShopFlowDefaults — no MediatR, no MassTransit, no outbox,
/// no DbContext"). Without the kernel composition the SignalR DI is
/// absent, so <c>MapHub</c> would throw at startup.</para>
///
/// <para>Transport selection: WebSockets first (the canonical bidirectional
/// transport), LongPolling as fallback for environments where the
/// upgrade fails. Server-Sent Events is excluded — the Sprint-7 hub is
/// server → client only but we keep LongPolling for symmetry with the
/// JS client's default behaviour and to support hosts that block WS.</para>
/// </remarks>
public static class SignalRRoutingExtensions
{
    /// <summary>
    /// Canonical hub path. Frontend builds its connection URL against
    /// this constant (mirrored in <c>web/src/lib/signalr.ts</c>).
    /// </summary>
    public const string HubPath = "/hub";

    /// <summary>
    /// Map <see cref="TenantHub"/> at <see cref="HubPath"/>. Call AFTER
    /// <c>app.MapControllers()</c> in the module's <c>Program.cs</c>.
    /// Only <c>Outbound.Api</c> should call this — see remarks on
    /// <see cref="SignalRRoutingExtensions"/>.
    /// </summary>
    public static IEndpointRouteBuilder MapShopFlowHubs(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        app.MapHub<TenantHub>(
            HubPath,
            options =>
            {
                options.Transports =
                    HttpTransportType.WebSockets | HttpTransportType.LongPolling;
            }
        );

        return app;
    }
}
