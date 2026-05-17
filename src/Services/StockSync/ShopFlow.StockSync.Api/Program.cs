using Hellang.Middleware.ProblemDetails;
using ShopFlow.ControlPlane.Infrastructure;
using ShopFlow.SharedKernel.Infrastructure;
using ShopFlow.StockSync.Application.Consumers;
using ShopFlow.StockSync.Infrastructure;

// ─────────────────────────────────────────────────────────────────────────
// ShopFlow.StockSync.Api — HTTP surface for the StockSync module
// (Phase-2 Sprint-5 plan U8). Composition order per AGENTS.md §11.79:
//   1. services.AddShopFlowDefaults(configuration)   — kernel cross-cutting
//      (MediatR, MassTransit + RabbitMQ, IRequestContext, OutboxInterceptor,
//      TenantRoutingMiddleware, OpenTelemetry, ProblemDetails). Scans the
//      StockSync.Application assembly so StockLevelChangedConsumer is
//      registered as a MassTransit consumer.
//   2. services.AddControlPlane(configuration)       — ITenantCatalog
//      backed by shopflow_control catalog DB. PerTenantDispatcherService +
//      MultiplexedOutboxDispatcher enumerate tenants through this port.
//   3. services.AddStockSyncModule(configuration)    — DbContext factory,
//      coalescing buffer + per-tenant queue + bucket + breaker registries,
//      hosted services (CoalesceFlushService + PerTenantDispatcherService
//      + MultiplexedOutboxDispatcher<StockSyncDbContext>), SkuFlag scoped
//      inner + singleton caching wrapper.
//
// SyncStateController carries [SkipTenantRouting] so the diagnostics endpoint
// can be polled without a tenant header (it returns process-level snapshots).
// SkuFlagsController goes through TenantRoutingMiddleware normally.
// ─────────────────────────────────────────────────────────────────────────

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddShopFlowDefaults(
    builder.Configuration,
    configure: o => o.ServiceName = "shopflow-stocksync",
    assembliesToScan: new[]
    {
        typeof(StockSyncDbContext).Assembly,
        typeof(StockLevelChangedConsumer).Assembly,
    }
);
builder.Services.AddControlPlane(builder.Configuration);
builder.Services.AddStockSyncModule(builder.Configuration);
builder.Services.AddControllers();

var app = builder.Build();
app.UseProblemDetails();
app.UseTenantRouting();
app.MapControllers();
await app.RunAsync().ConfigureAwait(false);

/// <summary>
/// Exposed as <c>public partial</c> so <c>WebApplicationFactory&lt;Program&gt;</c>
/// (Sprint-5 U8 <c>StockSyncApiCompositionTests</c> + U9 integration tests)
/// can boot the host in-process. Top-level program needs this shim because
/// the generated <c>Program</c> class is internal by default.
/// </summary>
public partial class Program;
