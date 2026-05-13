using ShopFlow.Inbound.Infrastructure;

// ─────────────────────────────────────────────────────────────────────────
// ShopFlow.Inbound.Api — HTTP surface for the Inbound module (Sprint-2-redux).
//
// U1 ships the composition root + the schema-only migration + a placeholder
// controller returning 501. Real PO + receiving endpoints land in U8.
//
// AddShopFlowDefaults wiring is deferred to Sprint-2-redux U7 (when the
// MassTransit transport flips from in-memory to RabbitMQ). Same gap exists
// on Inventory.Api; both get patched together in U7 so the kernel
// composition order (AGENTS.md §11.79) lands consistently.
// ─────────────────────────────────────────────────────────────────────────

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddInboundModule(builder.Configuration);
builder.Services.AddControllers();

var app = builder.Build();
app.MapControllers();
await app.RunAsync().ConfigureAwait(false);
