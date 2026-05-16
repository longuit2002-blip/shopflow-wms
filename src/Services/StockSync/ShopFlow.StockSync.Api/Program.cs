using Hellang.Middleware.ProblemDetails;
using ShopFlow.SharedKernel.Infrastructure;
using ShopFlow.StockSync.Infrastructure;

// ─────────────────────────────────────────────────────────────────────────
// ShopFlow.StockSync.Api — HTTP surface for the StockSync module.
// Sprint-5 plan U1 ships the scaffold (returns 501 on real endpoints); the
// full composition (AddControlPlane + AddStockSyncModule + UseTenantRouting
// + diagnostics endpoint) lands in U8 once the module's ports + services
// are in place (U3-U7).
// ─────────────────────────────────────────────────────────────────────────

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddShopFlowDefaults(
    builder.Configuration,
    configure: o => o.ServiceName = "shopflow-stocksync",
    assembliesToScan: new[]
    {
        typeof(StockSyncDbContext).Assembly,
    }
);
builder.Services.AddControllers();

var app = builder.Build();
app.UseProblemDetails();
app.MapControllers();
await app.RunAsync().ConfigureAwait(false);

/// <summary>
/// Exposed as <c>public partial</c> so <c>WebApplicationFactory&lt;Program&gt;</c>
/// (U9 integration tests) can boot the host in-process.
/// </summary>
public partial class Program;
