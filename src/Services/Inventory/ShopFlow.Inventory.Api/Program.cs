using Hellang.Middleware.ProblemDetails;
using ShopFlow.Inventory.Infrastructure;
using ShopFlow.SharedKernel.Infrastructure;

// ─────────────────────────────────────────────────────────────────────────
// ShopFlow.Inventory.Api — HTTP surface for the Inventory module.
// Composition order per AGENTS.md §11.79:
//   1. services.AddShopFlowDefaults(configuration)  — kernel-wide
//      cross-cutting (MediatR + behaviors, MassTransit + RabbitMQ
//      transport, IRequestContext, OutboxInterceptor wiring,
//      TenantRoutingMiddleware, OpenTelemetry, ProblemDetails,
//      JwtBearer authentication [Sprint-7 U5 lift], SignalR DI [U5]).
//      Sprint-2-redux U7 wires this in; the Inventory.Infrastructure
//      assembly is scanned so InboundConfirmedConsumer is registered.
//   2. services.AddInventoryModule(configuration)   — module specifics.
//
// Sprint-7 U5 — JwtBearer is now registered by AddShopFlowDefaults
// (Sprint-6 trade-off #8 closed); the previous per-module AddJwtBearer
// block here is removed. Inventory.Api intentionally does NOT call
// app.MapShopFlowHubs() — only Outbound.Api hosts the SignalR hub
// (single-hub-host decision per doc-review).
// ─────────────────────────────────────────────────────────────────────────

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddShopFlowDefaults(
    builder.Configuration,
    configure: o => o.ServiceName = "shopflow-inventory",
    assembliesToScan: new[]
    {
        typeof(ShopFlow.Inventory.Application.InventoryOptions).Assembly,
        typeof(ShopFlow.Inventory.Infrastructure.InventoryDbContext).Assembly,
    }
);
builder.Services.AddInventoryModule(builder.Configuration);

builder.Services.AddShopFlowControllers();

var app = builder.Build();
app.UseProblemDetails();
app.UseAuthentication();
app.UseAuthorization();
app.UseTenantRouting();
app.MapControllers();
await app.RunAsync().ConfigureAwait(false);

/// <summary>
/// Exposed as <c>public partial</c> so <c>WebApplicationFactory&lt;Program&gt;</c>
/// (Sprint-6 U7 + U8 controller tests) can boot the host in-process.
/// </summary>
public partial class Program;
