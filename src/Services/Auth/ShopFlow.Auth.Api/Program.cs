using Hellang.Middleware.ProblemDetails;
using ShopFlow.Auth.Api;
using ShopFlow.SharedKernel.Infrastructure;

// ─────────────────────────────────────────────────────────────────────────
// ShopFlow.Auth.Api — dev-mode fake login service (Sprint-6 plan U4).
//
// Sprint-7 swaps this for a real auth module with JWT issuance + refresh
// rotation + Redis-backed denylist + per-user persistence. The current
// surface accepts any non-empty (email, password) tuple and returns a
// baked JWT carrying tenant_slug = "yensaokhanhhoa" + role = "tenant_seller".
//
// NOT wired through AddShopFlowDefaults — no MediatR, no MassTransit, no
// outbox, no DbContext. Just controllers + options binding + ProblemDetails.
// Keep the surface minimal so Sprint-7 can drop it cleanly.
// ─────────────────────────────────────────────────────────────────────────

var builder = WebApplication.CreateBuilder(args);

builder.Services
    .AddOptions<AuthOptions>()
    .Bind(builder.Configuration.GetSection(AuthOptions.SectionName))
    .ValidateOnStart();

builder.Services.AddProblemDetails();
builder.Services.AddShopFlowControllers();

var app = builder.Build();

app.UseProblemDetails();
app.MapControllers();

await app.RunAsync().ConfigureAwait(false);

/// <summary>
/// Exposed as <c>public partial</c> so <c>WebApplicationFactory&lt;Program&gt;</c>
/// can boot the host in-process for the AuthControllerTests integration
/// suite (Sprint-6 plan U4).
/// </summary>
public partial class Program;
