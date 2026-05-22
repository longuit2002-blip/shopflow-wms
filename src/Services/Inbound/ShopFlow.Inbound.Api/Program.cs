using Hellang.Middleware.ProblemDetails;
using ShopFlow.Inbound.Infrastructure;
using ShopFlow.SharedKernel.Infrastructure;

// ─────────────────────────────────────────────────────────────────────────
// ShopFlow.Inbound.Api — HTTP surface for the Inbound module
// (Sprint-2-redux). Composition order per AGENTS.md §11.79:
//   1. services.AddShopFlowDefaults(configuration)  — kernel-wide
//      cross-cutting (MediatR + behaviors, MassTransit + transport
//      selection, IRequestContext, OutboxInterceptor wiring,
//      TenantRoutingMiddleware, OpenTelemetry, ProblemDetails)
//   2. services.AddInboundModule(configuration)     — module specifics
//      (InboundDbContext, repositories, MultiplexedOutboxDispatcher
//      hosted service, ConfirmReceivingLineService)
//
// Sprint-10 KTD3 — UseAuthentication() + UseAuthorization() are now wired
// here (they were missing pre-Sprint-10, leaving PurchaseOrdersController
// unauthenticated). Kept hand-wired (not via UseShopFlowSecurityPipeline)
// per KTD4 for cross-business-module consistency with Inventory/Outbound.
// ─────────────────────────────────────────────────────────────────────────

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddShopFlowDefaults(
    builder.Configuration,
    configure: o => o.ServiceName = "shopflow-inbound",
    assembliesToScan: new[]
    {
        typeof(ShopFlow.Inbound.Application.Services.ConfirmReceivingLineService).Assembly,
        typeof(ShopFlow.Inbound.Infrastructure.InboundDbContext).Assembly,
    }
);
builder.Services.AddInboundModule(builder.Configuration);
builder.Services.AddShopFlowControllers();

var app = builder.Build();
app.UseProblemDetails();
app.UseAuthentication();
app.UseAuthorization();
app.UseTenantRouting();
app.MapControllers();
await app.RunAsync().ConfigureAwait(false);

/// <summary>
/// Sprint-10.5 U4 — exposed as <c>public partial</c> so
/// <c>WebApplicationFactory&lt;Program&gt;</c> can boot the Inbound host
/// in-process for the 403 wire-shape integration suite under
/// <c>tests/ShopFlow.Inbound.IntegrationTests/Authorization/</c>.
/// </summary>
public partial class Program;
