using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;

namespace ShopFlow.SharedKernel.Infrastructure;

/// <summary>
/// Single-call helper that bundles <c>AddControllers</c> with the
/// ShopFlow-wide camelCase JSON convention (Sprint-7.5 #2 — wire-format
/// normalisation). Every module API calls this in place of
/// <c>AddControllers</c> so the wire shape is consistent across the
/// surface and every frontend slice (Inventory, Orders, future Inbound /
/// Channel / Analytics / Settings) reads camelCase from day one.
/// </summary>
/// <remarks>
/// Mirrors the kernel-wide pattern from Sprint-7 U5 (JwtBearer lifted
/// into <see cref="ShopFlowDefaultsExtensions.AddShopFlowDefaults"/>):
/// keep cross-cutting configuration in one composition point so a single
/// future change reaches all module Apis. Naming policy matches
/// <see cref="OutboxJsonOptions.Default"/> so the wire format is
/// symmetrical to the outbox payload format already in use.
/// </remarks>
public static class AddShopFlowControllersExtensions
{
    /// <summary>
    /// Registers MVC controllers + the ShopFlow JSON convention
    /// (camelCase property naming, case-insensitive deserialize so
    /// PascalCase legacy payloads still round-trip). Returns the
    /// <see cref="IMvcBuilder"/> so callers can chain further
    /// MVC-specific configuration (e.g., <c>AddApplicationPart</c>).
    /// </summary>
    public static IMvcBuilder AddShopFlowControllers(this IServiceCollection services)
    {
        return services
            .AddControllers()
            .AddJsonOptions(static opts =>
            {
                opts.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
                // Outbox shape parity: lenient deserialize covers
                // round-trips with older clients still emitting
                // PascalCase. Cheap insurance; matches OutboxJsonOptions.
                opts.JsonSerializerOptions.PropertyNameCaseInsensitive = true;
            });
    }
}
