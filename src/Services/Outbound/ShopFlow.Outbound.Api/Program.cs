using Hellang.Middleware.ProblemDetails;
using ShopFlow.Outbound.Infrastructure;
using ShopFlow.SharedKernel.Infrastructure;

// ─────────────────────────────────────────────────────────────────────────
// ShopFlow.Outbound.Api — HTTP surface for the Outbound module
// (Sprint-3-redux). Composition order per AGENTS.md §11.79:
//   1. services.AddShopFlowDefaults(configuration)  — kernel-wide
//      cross-cutting (MediatR + behaviors, MassTransit + transport
//      selection, IRequestContext, OutboxInterceptor wiring,
//      TenantRoutingMiddleware, OpenTelemetry, ProblemDetails)
//   2. services.AddOutboundModule(configuration)    — module specifics
//      (OutboundDbContext, MultiplexedOutboxDispatcher hosted service;
//      saga / pick queue / mock carrier register in U4-U6)
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
app.UseTenantRouting();
app.MapControllers();
await app.RunAsync().ConfigureAwait(false);
