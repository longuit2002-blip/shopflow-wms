using ShopFlow.Inventory.Infrastructure;

// ─────────────────────────────────────────────────────────────────────────
// ShopFlow.Inventory.Api — HTTP surface for the Inventory module (plan U8).
//
// U8 ships the composition root + a placeholder controller that returns
// 501 Not Implemented. The real reservation / availability endpoints land
// in Sprint-1-redux (docs/plans/2026-05-11-003-phase-1-sprint-1-redux-...).
//
// The composition order is canon per AGENTS.md §11.79: kernel defaults
// first (routing middleware, IRequestContext, OutboxInterceptor wiring,
// MediatR pipeline behaviours), then the module-specific
// AddInventoryModule. Wrong-order registrations surface as a
// missing-dependency exception at first request, not at startup —
// document this here so future modules don't shuffle.
// ─────────────────────────────────────────────────────────────────────────

var builder = WebApplication.CreateBuilder(args);

// U8: kernel-defaults wiring lands once SharedKernel ships AddShopFlowDefaults
// in its public Infrastructure surface. The current SharedKernel exposes
// the pieces (TenantRoutingMiddleware, IRequestContext, etc.) individually;
// U9-U10 introduce a single composition entry point.

builder.Services.AddInventoryModule(builder.Configuration);
builder.Services.AddControllers();

var app = builder.Build();
app.MapControllers();
await app.RunAsync().ConfigureAwait(false);
