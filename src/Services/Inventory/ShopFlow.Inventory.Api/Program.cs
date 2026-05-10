using System.Text;
using Hellang.Middleware.ProblemDetails;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using ShopFlow.Inventory.Application.Handlers;
using ShopFlow.Inventory.Infrastructure;
using ShopFlow.SharedKernel.Infrastructure;

namespace ShopFlow.Inventory.Api;

/// <summary>
/// Inventory module's host entry point. Wires the SharedKernel defaults
/// (OpenTelemetry, MediatR, FluentValidation, MassTransit, ProblemDetails),
/// the Inventory module composition, and JWT bearer auth.
/// </summary>
public static class Program
{
    public static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        builder.Services.AddShopFlowDefaults(
            builder.Configuration,
            options => options.ServiceName = "shopflow-inventory",
            typeof(ReserveStockHandler).Assembly
        );

        builder.Services.AddInventoryModule(builder.Configuration);

        ConfigureAuthentication(builder);

        builder.Services.AddAuthorization();
        builder.Services.AddControllers();
        builder.Services.AddEndpointsApiExplorer();
        builder.Services.AddHealthChecks();

        // Register the polling outbox dispatcher for the Inventory DbContext.
        builder.Services.AddHostedService<OutboxDispatcher<InventoryDbContext>>();

        var app = builder.Build();

        app.UseProblemDetails();
        app.UseAuthentication();
        app.UseAuthorization();

        app.MapControllers();
        app.MapHealthChecks("/healthz");

        app.Run();
    }

    /// <summary>
    /// JWT bearer wiring. In Phase 0 the issuer is unconfigured for local
    /// development (no Authority); a symmetric development signing key is
    /// used so smoke tests against <c>/api/inventory/*</c> can mint tokens
    /// without a real STS. Production deployments override the
    /// <c>Jwt:Authority</c> to point at the real identity provider.
    /// </summary>
    private static void ConfigureAuthentication(WebApplicationBuilder builder)
    {
        var jwtSection = builder.Configuration.GetSection("Jwt");
        var authority = jwtSection.GetValue<string>("Authority");
        var audience = jwtSection.GetValue<string>("Audience") ?? "shopflow-inventory";
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
                    // Dev-only fallback: validate against a symmetric key so
                    // we don't need a running STS for local smoke tests.
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
}
