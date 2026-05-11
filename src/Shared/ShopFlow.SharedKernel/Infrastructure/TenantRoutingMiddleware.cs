using System.Diagnostics;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using ShopFlow.SharedKernel.Application;
using ShopFlow.SharedKernel.Application.Ports;

namespace ShopFlow.SharedKernel.Infrastructure;

/// <summary>
/// The tenant-correctness primitive under ADR-0003. Resolves a tenant slug
/// from header / JWT / subdomain (priority order, conflicts rejected with
/// 403), looks the tenant up via <see cref="ITenantCatalog"/>, and populates
/// <see cref="RequestContext"/> for the request scope.
/// </summary>
/// <remarks>
/// <para>Resolution rules per Phase-0-redux deferred-item D4:</para>
/// <list type="bullet">
///   <item><description>Priority: <c>X-ShopFlow-Tenant</c> header &gt; JWT <c>tenant_id</c> claim &gt; subdomain.</description></item>
///   <item><description>All sources that produce a slug must agree. Any 2+ disagreement returns 403; the conflict is logged to the control-plane <c>tenant_events</c> table (out of scope for Phase-0-redux — placeholder log emit only).</description></item>
///   <item><description>No source present: 400.</description></item>
///   <item><description>Slug unknown to catalog: 404.</description></item>
///   <item><description>Slug found but <c>Status != Ready</c>: 503.</description></item>
/// </list>
/// </remarks>
public sealed class TenantRoutingMiddleware
{
    public const string TenantHeader = "X-ShopFlow-Tenant";
    public const string CorrelationHeader = "X-Correlation-Id";
    public const string JwtTenantClaim = "tenant_slug";
    public const string ActivityTenantTag = "tenant.id";
    public const string ActivityTenantSlugTag = "tenant.slug";

    private readonly RequestDelegate _next;
    private readonly ILogger<TenantRoutingMiddleware> _logger;

    public TenantRoutingMiddleware(RequestDelegate next, ILogger<TenantRoutingMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(
        HttpContext context,
        ITenantCatalog catalog,
        RequestContext requestContext
    )
    {
        var slugs = ExtractSlugs(context);
        var slug = ResolveSlug(slugs, out var conflict);

        if (conflict)
        {
            _logger.LogWarning(
                "Tenant routing conflict: header={Header} jwt={Jwt} subdomain={Subdomain}",
                slugs.Header,
                slugs.Jwt,
                slugs.Subdomain
            );
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            await context.Response.WriteAsync("tenant routing conflict");
            return;
        }

        if (slug is null)
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            await context.Response.WriteAsync("missing tenant context");
            return;
        }

        var tenant = await catalog.LookupBySlugAsync(slug, context.RequestAborted);
        if (tenant is null)
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            await context.Response.WriteAsync("tenant not found");
            return;
        }

        if (tenant.Status != TenantStatus.Ready)
        {
            context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
            await context.Response.WriteAsync("tenant not ready");
            return;
        }

        var correlationId =
            context.Request.Headers.TryGetValue(CorrelationHeader, out var hdr) && hdr.Count > 0
                ? hdr.ToString()
                : Activity.Current?.TraceId.ToString()
                    ?? Guid.NewGuid().ToString("N");

        var userId = ExtractUserId(context.User);

        requestContext.Bind(tenant, correlationId, userId);

        Activity.Current?.SetTag(ActivityTenantTag, tenant.Id.ToString());
        Activity.Current?.SetTag(ActivityTenantSlugTag, tenant.Slug);

        await _next(context);
    }

    private static SlugSources ExtractSlugs(HttpContext context)
    {
        var headerSlug =
            context.Request.Headers.TryGetValue(TenantHeader, out var hdr) && hdr.Count > 0
                ? Normalize(hdr.ToString())
                : null;

        var jwtSlug = context
            .User?.FindFirst(JwtTenantClaim)
            ?.Value is { Length: > 0 } jwt
            ? Normalize(jwt)
            : null;

        var subdomain = ExtractSubdomain(context.Request.Host.Host);

        return new SlugSources(headerSlug, jwtSlug, subdomain);
    }

    private static string? ResolveSlug(SlugSources sources, out bool conflict)
    {
        conflict = false;
        var present = new[] { sources.Header, sources.Jwt, sources.Subdomain }
            .Where(s => s is { Length: > 0 })
            .ToArray();

        if (present.Length == 0)
        {
            return null;
        }

        var first = present[0]!;
        for (var i = 1; i < present.Length; i++)
        {
            if (!string.Equals(present[i], first, StringComparison.OrdinalIgnoreCase))
            {
                conflict = true;
                return null;
            }
        }

        // Priority winner is the first non-null in header > jwt > subdomain order.
        return sources.Header ?? sources.Jwt ?? sources.Subdomain;
    }

    private static string? ExtractSubdomain(string host)
    {
        // Format: <slug>.shopflow.local | <slug>.shopflow.example
        if (string.IsNullOrEmpty(host))
        {
            return null;
        }

        var firstDot = host.IndexOf('.');
        if (firstDot <= 0)
        {
            return null;
        }

        var candidate = host[..firstDot];
        // Reject common non-tenant subdomains
        if (candidate is "www" or "api" or "localhost" or "admin")
        {
            return null;
        }

        return Normalize(candidate);
    }

    private static Guid? ExtractUserId(ClaimsPrincipal? user)
    {
        if (user?.Identity?.IsAuthenticated != true)
        {
            return null;
        }

        // Standard JWT subject claim — string literal to avoid depending on
        // System.IdentityModel.Tokens.Jwt at the kernel level. Modules that
        // expose authenticated endpoints add the JwtBearer auth scheme on
        // top; the kernel only reads the resolved ClaimsPrincipal.
        var sub = user.FindFirst("sub")?.Value
            ?? user.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return Guid.TryParse(sub, out var id) ? id : null;
    }

    private static string Normalize(string slug) => slug.Trim().ToLowerInvariant();

    private readonly record struct SlugSources(string? Header, string? Jwt, string? Subdomain);
}
