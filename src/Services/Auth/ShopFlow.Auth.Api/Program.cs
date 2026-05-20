using Hellang.Middleware.ProblemDetails;
using ShopFlow.Auth.Api;
using ShopFlow.Auth.Infrastructure;
using ShopFlow.ControlPlane.Infrastructure;
using ShopFlow.SharedKernel.Infrastructure;

// ─────────────────────────────────────────────────────────────────────────
// ShopFlow.Auth.Api — real auth surface (Sprint-8 U9). Composition order
// per AGENTS.md §11.79:
//   1. services.AddShopFlowDefaults(configuration)   — kernel cross-cutting
//      (MediatR, MassTransit + RabbitMQ, IRequestContext,
//      OutboxInterceptor, TenantRoutingMiddleware, OpenTelemetry,
//      ProblemDetails, JwtBearer validation against the SAME Auth:DevSecret
//      this module signs with via JwtTokenIssuer)
//   2. services.AddControlPlane(configuration)       — ITenantCatalog
//      for the in-controller subdomain resolver
//   3. services.AddAuthModule(configuration)         — AuthDbContext,
//      UserRepository, Argon2idPasswordHasher, RedisRefreshTokenStore,
//      JwtTokenIssuer, PasswordGenerator
//   4. services.AddShopFlowControllers()             — Sprint-7.5 camelCase
//      JSON helper
//
// Replaces the Sprint-6 dev-mode fake login stub. The AuthOptions class
// loses its DemoRole + DemoTenantSlug fields; AuthController now resolves
// tenant from Host subdomain or body fallback (R5).
// ─────────────────────────────────────────────────────────────────────────

var builder = WebApplication.CreateBuilder(args);

builder.Services
    .AddOptions<AuthOptions>()
    .Bind(builder.Configuration.GetSection(AuthOptions.SectionName))
    .ValidateOnStart();

builder.Services.AddShopFlowDefaults(
    builder.Configuration,
    configure: o => o.ServiceName = "shopflow-auth",
    assembliesToScan: new[]
    {
        typeof(ShopFlow.Auth.Application.Commands.LoginCommand).Assembly,
        typeof(AuthDbContext).Assembly,
    });
builder.Services.AddControlPlane(builder.Configuration);
builder.Services.AddAuthModule(builder.Configuration);
builder.Services.AddShopFlowControllers();

var app = builder.Build();

app.UseProblemDetails();
// Sprint-9 U7 — ForwardedHeaders + RateLimiter BEFORE Authentication so
// the rate-limit partition key reads the real client IP from
// X-Forwarded-For (per KTD7).
app.UseShopFlowSecurityPipeline();
app.UseAuthentication();
app.UseAuthorization();
app.UseTenantRouting();
app.MapControllers();

await app.RunAsync().ConfigureAwait(false);

/// <summary>
/// Exposed as <c>public partial</c> so <c>WebApplicationFactory&lt;Program&gt;</c>
/// can boot the host in-process for the integration suite under
/// <c>tests/ShopFlow.Auth.IntegrationTests/</c>.
/// </summary>
public partial class Program;
