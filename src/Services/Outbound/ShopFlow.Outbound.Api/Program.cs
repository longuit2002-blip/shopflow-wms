using Hellang.Middleware.ProblemDetails;
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
    configure: o => o.ServiceName = "shopflow-outbound",
    assembliesToScan: new[]
    {
        typeof(ShopFlow.Outbound.Infrastructure.OutboundDbContext).Assembly,
    }
);
builder.Services.AddOutboundModule(builder.Configuration);
builder.Services.AddControllers();

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
