using Hellang.Middleware.ProblemDetails;
using ShopFlow.Channel.Infrastructure;
using ShopFlow.ControlPlane.Infrastructure;
using ShopFlow.SharedKernel.Infrastructure;

// ─────────────────────────────────────────────────────────────────────────
// ShopFlow.Channel.Api — HTTP surface for the Channel module
// (Phase-2 Sprint-4 plan U9). Composition order per AGENTS.md §11.79:
//   1. services.AddShopFlowDefaults(configuration)   — kernel cross-cutting
//      (MediatR, MassTransit + RabbitMQ, IRequestContext, OutboxInterceptor,
//      TenantRoutingMiddleware, OpenTelemetry, ProblemDetails)
//   2. services.AddControlPlane(configuration)       — ITenantCatalog +
//      IChannelDirectory backed by shopflow_control catalog DB
//   3. services.AddChannelModule(configuration)      — DbContext factory,
//      webhook orchestrator, Shopee adapter + signature verifier, K13
//      OrderImportedV1 Send-routing, MultiplexedOutboxDispatcher<ChannelDbContext>
//
// Webhook endpoints carry [SkipTenantRouting] so UseTenantRouting bypasses
// them (channel_id-driven tenant resolution happens inside WebhooksController
// after HMAC verification clears).
// ─────────────────────────────────────────────────────────────────────────

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddShopFlowDefaults(
    builder.Configuration,
    configure: o => o.ServiceName = "shopflow-channel",
    assembliesToScan: new[]
    {
        typeof(ChannelDbContext).Assembly,
    }
);
builder.Services.AddControlPlane(builder.Configuration);
builder.Services.AddChannelModule(builder.Configuration);
builder.Services.AddControllers();

var app = builder.Build();
app.UseProblemDetails();
app.UseTenantRouting();
app.MapControllers();
await app.RunAsync().ConfigureAwait(false);
