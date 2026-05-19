// ─────────────────────────────────────────────────────────────────────────
// ShopFlow.Analytics.Api — placeholder host (plan U9). All endpoints return 501.
// Composition root expands when the module's first real handler lands
// (Phase-1+); U9 ships the empty shape so the AGENTS.md §11.79 ordering
// (AddShopFlowDefaults then Add<Name>Module) is locked into CI.
// ─────────────────────────────────────────────────────────────────────────

using ShopFlow.SharedKernel.Infrastructure;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddShopFlowControllers();
var app = builder.Build();
app.MapControllers();
await app.RunAsync().ConfigureAwait(false);
