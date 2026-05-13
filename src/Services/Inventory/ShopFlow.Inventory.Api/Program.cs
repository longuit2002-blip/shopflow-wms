using Hellang.Middleware.ProblemDetails;
using ShopFlow.Inventory.Infrastructure;
using ShopFlow.SharedKernel.Infrastructure;

// ─────────────────────────────────────────────────────────────────────────
// ShopFlow.Inventory.Api — HTTP surface for the Inventory module.
// Composition order per AGENTS.md §11.79:
//   1. services.AddShopFlowDefaults(configuration)  — kernel-wide
//      cross-cutting (MediatR + behaviors, MassTransit + RabbitMQ
//      transport, IRequestContext, OutboxInterceptor wiring,
//      TenantRoutingMiddleware, OpenTelemetry, ProblemDetails). Sprint-2-redux
//      U7 wires this in; the Inventory.Infrastructure assembly is scanned
//      so InboundConfirmedConsumer is registered.
//   2. services.AddInventoryModule(configuration)   — module specifics.
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
builder.Services.AddControllers();

var app = builder.Build();
app.UseProblemDetails();
app.UseTenantRouting();
app.MapControllers();
await app.RunAsync().ConfigureAwait(false);
