using System.Text;
using Hellang.Middleware.ProblemDetails;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using ShopFlow.Inventory.Infrastructure;
using ShopFlow.SharedKernel.Infrastructure;

// ─────────────────────────────────────────────────────────────────────────
// ShopFlow.Inventory.Api — HTTP surface for the Inventory module.
// Composition order per AGENTS.md §11.79:
//   1. services.AddShopFlowDefaults(configuration)  — kernel-wide
//      cross-cutting (MediatR + behaviors, MassTransit + RabbitMQ
//      transport, IRequestContext, OutboxInterceptor wiring,
//      TenantRoutingMiddleware, OpenTelemetry, ProblemDetails). Sprint-2-redux
//      U7 wires this in; the Inventory.Infrastructure assembly is scanned
//      so InboundConfirmedConsumer is registered.
//   2. services.AddInventoryModule(configuration)   — module specifics.
//   3. Sprint-6 U4 — JwtBearer authentication scheme reading the same
//      Auth:DevSecret + Issuer + Audience that Auth.Api signs with.
//      TenantRoutingMiddleware reads the `tenant_slug` claim once a JWT
//      validates.
// ─────────────────────────────────────────────────────────────────────────

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddShopFlowDefaults(
    builder.Configuration,
    configure: o => o.ServiceName = "shopflow-inventory",
    assembliesToScan: new[]
    {
        typeof(ShopFlow.Inventory.Application.InventoryOptions).Assembly,
        typeof(ShopFlow.Inventory.Infrastructure.InventoryDbContext).Assembly,
    }
);
builder.Services.AddInventoryModule(builder.Configuration);

// Sprint-6 U4 — JwtBearer. Sprint-7 swaps Auth:DevSecret for a real signer.
var devSecret = builder.Configuration["Auth:DevSecret"]
    ?? throw new InvalidOperationException(
        "Auth:DevSecret missing. Sprint-6 dev mode expects a shared secret with Auth.Api.");
var issuer = builder.Configuration["Auth:Issuer"] ?? "shopflow-dev";
var audience = builder.Configuration["Auth:Audience"] ?? "shopflow-api";

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(opts =>
    {
        opts.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = issuer,
            ValidateAudience = true,
            ValidAudience = audience,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(devSecret)),
            ClockSkew = TimeSpan.FromSeconds(30),
        };
    });
builder.Services.AddAuthorization();

builder.Services.AddControllers();

var app = builder.Build();
app.UseProblemDetails();
app.UseAuthentication();
app.UseAuthorization();
app.UseTenantRouting();
app.MapControllers();
await app.RunAsync().ConfigureAwait(false);

/// <summary>
/// Exposed as <c>public partial</c> so <c>WebApplicationFactory&lt;Program&gt;</c>
/// (Sprint-6 U7 + U8 controller tests) can boot the host in-process.
/// </summary>
public partial class Program;
