using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using ShopFlow.SharedKernel.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddShopFlowDefaults(
    builder.Configuration,
    options => options.ServiceName = "shopflow-gateway"
);

// YARP reverse-proxy route table loaded from the "ReverseProxy" section.
// Cluster destinations match Aspire service-discovery names (e.g.
// http://inventory-api) so the AppHost wiring just works in dev. Production
// deployment swaps the destinations via configuration without code changes.
builder
    .Services.AddReverseProxy()
    .LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"));

ConfigureAuthentication(builder);

builder.Services.AddAuthorization();
builder.Services.AddHealthChecks();

var app = builder.Build();

app.UseAuthentication();
app.UseAuthorization();

// Gateway's own health endpoint — answered locally, never proxied. Per
// AGENTS.md §11 every module exposes /healthz; the gateway is no exception.
app.MapHealthChecks("/healthz");

app.MapReverseProxy();

app.Run();

/// <summary>
/// JWT bearer wiring for the gateway boundary. Mirrors the per-module
/// pattern in Inventory.Api so the validated <c>IRequestContext</c>
/// trusted by downstream services has the same shape regardless of
/// whether traffic flows through the gateway or hits a module directly
/// (the latter is a dev-only convenience).
/// </summary>
static void ConfigureAuthentication(WebApplicationBuilder builder)
{
    var jwtSection = builder.Configuration.GetSection("Jwt");
    var authority = jwtSection.GetValue<string>("Authority");
    var audience = jwtSection.GetValue<string>("Audience") ?? "shopflow-gateway";
    var requireHttps = jwtSection.GetValue<bool>("RequireHttpsMetadata");
    var devSigningKey = jwtSection.GetValue<string>("DevelopmentSigningKey");

    builder
        .Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
        .AddJwtBearer(options =>
        {
            options.Audience = audience;
            options.RequireHttpsMetadata = requireHttps;

            if (!string.IsNullOrWhiteSpace(authority))
            {
                options.Authority = authority;
            }
            else if (!string.IsNullOrWhiteSpace(devSigningKey))
            {
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = false,
                    ValidateAudience = true,
                    ValidAudience = audience,
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = new SymmetricSecurityKey(
                        Encoding.UTF8.GetBytes(devSigningKey)
                    ),
                };
            }
        });
}

namespace ShopFlow.Gateway
{
    /// <summary>
    /// Marker class for <c>WebApplicationFactory&lt;Program&gt;</c>
    /// integration tests against the gateway.
    /// </summary>
    public partial class Program { }
}
