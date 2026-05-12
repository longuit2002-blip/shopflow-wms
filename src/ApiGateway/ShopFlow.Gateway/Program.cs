// ─────────────────────────────────────────────────────────────────────────
// ShopFlow.Gateway — YARP reverse proxy in front of the module APIs
// (plan U9). In the W1-W5 modular-monolith stance the routes target
// in-process upstream URLs (or, more typically, sibling containers in
// the dev orchestrator); at W6 the same routes flip to the split-process
// host URLs and nothing else changes.
//
// The tenant routing middleware from the SharedKernel runs first. The
// downstream module sees the same X-ShopFlow-Tenant / JWT signal intact —
// YARP forwards headers by default — so AGENTS.md §3.15 holds without
// the gateway having to re-implement extraction.
// ─────────────────────────────────────────────────────────────────────────

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddReverseProxy().LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"));

var app = builder.Build();

app.MapReverseProxy();

await app.RunAsync().ConfigureAwait(false);
