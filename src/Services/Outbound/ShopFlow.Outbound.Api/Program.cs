using Hellang.Middleware.ProblemDetails;
using ShopFlow.ControlPlane.Infrastructure;
using ShopFlow.Outbound.Infrastructure;
using ShopFlow.SharedKernel.Infrastructure;
using ShopFlow.SharedKernel.Infrastructure.SignalR;

// ─────────────────────────────────────────────────────────────────────────
// ShopFlow.Outbound.Api — HTTP surface for the Outbound module
// (Sprint-3-redux). Composition order per AGENTS.md §11.79:
//   1. services.AddShopFlowDefaults(configuration)  — kernel-wide
//      cross-cutting (MediatR + behaviors, MassTransit + transport
//      selection, IRequestContext, OutboxInterceptor wiring,
//      TenantRoutingMiddleware, OpenTelemetry, ProblemDetails,
//      JwtBearer auth [Sprint-7 U5], SignalR DI [Sprint-7 U5])
//   2. services.AddOutboundModule(configuration)    — module specifics
//      (OutboundDbContext, MultiplexedOutboxDispatcher hosted service;
//      saga / pick queue / mock carrier register in U4-U6)
//
// Sprint-7 U5 — Outbound.Api is the SOLE hub-host process per the
// single-hub-host doc-review decision. The Gateway routes /hub to
// outbound-api; relay consumers in SharedKernel (U6) push to the hub
// via IHubContext<TenantHub>. Mapping the hub on every module API
// would create a RabbitMQ competing-consumer trap on the eventual W6
// split — each event would arrive at one arbitrary process while the
// client is connected to another.
// ─────────────────────────────────────────────────────────────────────────

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddShopFlowDefaults(
    builder.Configuration,
    configure: o =>
    {
        o.ServiceName = "shopflow-outbound";
        // Finish-line U4 — register the FulfillmentSaga EF repository + the
        // SignalR relay consumers inside the kernel's SINGLE AddMassTransit
        // (MassTransit forbids a second AddMassTransit per container). This
        // replaces the second AddMassTransit that AddOutboundModule used to
        // call, which threw at host build and kept Outbound.Api from ever
        // booting. See OutboundServiceCollectionExtensions.ConfigureOutboundBus.
        o.ConfigureBus = ShopFlow
            .Outbound
            .Infrastructure
            .OutboundServiceCollectionExtensions
            .ConfigureOutboundBus;
    },
    assembliesToScan: new[]
    {
        // Sprint-7 U4 — Outbound.Application assembly carries the MediatR
        // queries (ListOrdersQuery / GetOrderDetailQuery /
        // GetOrderTransitionsQuery) + their handlers introduced in U3.
        // Without this entry the MediatR RegisterServicesFromAssemblies
        // scan only sees Infrastructure, the handlers don't resolve, and
        // OrdersController.ListAsync (U4) throws at _mediator.Send time.
        // Mirrors Inventory.Api's pattern of scanning the Application
        // assembly alongside Infrastructure.
        typeof(ShopFlow.Outbound.Application.PickRequestV1).Assembly,
        typeof(ShopFlow.Outbound.Infrastructure.OutboundDbContext).Assembly,
    }
);

// Finish-line U4 — register ITenantCatalog (backed by the shopflow_control
// catalog DB). TenantRoutingMiddleware, the SignalR hub filter, and the relay
// consumers all depend on it. Outbound.Api never wired this because the host
// never booted (the double-AddMassTransit threw first); mirrors StockSync.Api.
builder.Services.AddControlPlane(builder.Configuration);
builder.Services.AddOutboundModule(builder.Configuration);
builder.Services.AddShopFlowControllers();

var app = builder.Build();
app.UseProblemDetails();
app.UseAuthentication();
app.UseAuthorization();
app.UseTenantRouting();
app.MapControllers();
app.MapShopFlowHubs();
await app.RunAsync().ConfigureAwait(false);

/// <summary>
/// Exposed as <c>public partial</c> so <c>WebApplicationFactory&lt;Program&gt;</c>
/// (Sprint-7 U6 SignalR relay integration tests) can boot the host in-process.
/// </summary>
public partial class Program;
