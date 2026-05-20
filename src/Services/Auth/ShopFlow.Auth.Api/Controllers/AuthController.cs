using System.Security.Claims;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using ShopFlow.Auth.Application.Commands;
using ShopFlow.Auth.Application.Dtos;
using ShopFlow.ControlPlane.Application.Ports;
using ShopFlow.SharedKernel.Application;
using ShopFlow.SharedKernel.Application.Ports;
using ShopFlow.SharedKernel.Infrastructure;

namespace ShopFlow.Auth.Api.Controllers;

/// <summary>
/// Sprint-8 U9 — real auth surface. Replaces the Sprint-6 dev-mode
/// fake login. Endpoints:
/// <list type="bullet">
///   <item><c>POST /api/auth/login</c> — credential check + token pair.</item>
///   <item><c>POST /api/auth/refresh</c> — rotate refresh token.</item>
///   <item><c>POST /api/auth/logout</c> — revoke refresh.</item>
///   <item><c>POST /api/auth/me/password</c> — self-service rotation.</item>
/// </list>
/// </summary>
/// <remarks>
/// <para>Bypasses <see cref="TenantRoutingMiddleware"/> via
/// <see cref="SkipTenantRoutingAttribute"/> at the class level — auth
/// endpoints run BEFORE the access-token JWT exists, so the
/// middleware's header > JWT > subdomain priority doesn't apply. R5
/// subdomain-first priority is implemented inside the controller via
/// <see cref="ResolveTenantAsync"/>.</para>
///
/// <para>All credential failure modes (missing user, inactive user,
/// wrong password, unknown tenant) collapse to the same response
/// shape — <c>401 + auth.invalid_credentials</c> — to close
/// enumeration side channels (R6 + ADV-004).</para>
/// </remarks>
[ApiController]
[Route("api/auth")]
[SkipTenantRouting]
public sealed class AuthController : ControllerBase
{
    private readonly IMediator _mediator;
    private readonly ITenantCatalog _tenantCatalog;
    private readonly RequestContext _requestContext;
    private readonly AuthOptions _authOptions;

    public AuthController(
        IMediator mediator,
        ITenantCatalog tenantCatalog,
        RequestContext requestContext,
        IOptions<AuthOptions> authOptions)
    {
        _mediator = mediator;
        _tenantCatalog = tenantCatalog;
        _requestContext = requestContext;
        _authOptions = authOptions.Value;
    }

    [AllowAnonymous]
    [HttpPost("login")]
    [ProducesResponseType(typeof(LoginResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Login([FromBody] LoginRequestDto? body, CancellationToken ct)
    {
        if (body is null)
        {
            return ValidationProblem("Request body is required.");
        }

        var slugResult = await ResolveTenantAsync(body.TenantSlug, ct).ConfigureAwait(false);
        if (!slugResult.Success)
        {
            return slugResult.ErrorResult!;
        }

        var result = await _mediator
            .Send(new LoginCommand(body.Email, body.Password, body.RememberMe, slugResult.Slug!), ct)
            .ConfigureAwait(false);

        return result.IsSuccess
            ? Ok(result.Value)
            : Unauthorized(new ProblemDetails
            {
                Title = "Invalid credentials.",
                Status = StatusCodes.Status401Unauthorized,
                Type = "auth.invalid_credentials",
            });
    }

    [AllowAnonymous]
    [HttpPost("refresh")]
    [ProducesResponseType(typeof(RefreshResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Refresh(
        [FromBody] RefreshRequestDto? body,
        CancellationToken ct)
    {
        if (body is null || string.IsNullOrWhiteSpace(body.RefreshToken))
        {
            return ValidationProblem("refresh_token is required.");
        }

        var slugResult = await ResolveTenantAsync(body.TenantSlug, ct).ConfigureAwait(false);
        if (!slugResult.Success)
        {
            return slugResult.ErrorResult!;
        }

        var result = await _mediator
            .Send(new RefreshTokenCommand(body.RefreshToken, body.UserId, slugResult.Slug!), ct)
            .ConfigureAwait(false);

        if (result.IsSuccess)
        {
            return Ok(result.Value);
        }

        return Unauthorized(new ProblemDetails
        {
            Title = result.Error ?? "Invalid credentials.",
            Status = StatusCodes.Status401Unauthorized,
            Type = result.ErrorCode ?? "auth.invalid_credentials",
        });
    }

    [Authorize]
    [HttpPost("logout")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Logout([FromBody] LogoutRequestDto? body, CancellationToken ct)
    {
        if (body is null || string.IsNullOrWhiteSpace(body.RefreshToken))
        {
            return ValidationProblem("refresh_token is required.");
        }

        if (!TryReadAuthenticatedTenant(out var slug, out var userId))
        {
            return Unauthorized();
        }

        await _mediator
            .Send(new LogoutCommand(body.RefreshToken, body.AllDevices, userId, slug), ct)
            .ConfigureAwait(false);

        return NoContent();
    }

    [Authorize]
    [HttpPost("me/password")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> ChangeMyPassword(
        [FromBody] ChangePasswordRequest? body,
        CancellationToken ct)
    {
        if (body is null)
        {
            return ValidationProblem("Request body is required.");
        }

        if (!TryReadAuthenticatedTenant(out var slug, out var userId))
        {
            return Unauthorized();
        }

        var result = await _mediator
            .Send(new ChangePasswordCommand(body.CurrentPassword, body.NewPassword, userId, slug), ct)
            .ConfigureAwait(false);

        if (result.IsSuccess)
        {
            return NoContent();
        }

        var status = result.ErrorCode switch
        {
            "auth.password_too_short" => StatusCodes.Status400BadRequest,
            _ => StatusCodes.Status401Unauthorized,
        };
        return StatusCode(status, new ProblemDetails
        {
            Title = result.Error,
            Status = status,
            Type = result.ErrorCode,
        });
    }

    // ───────────── Tenant resolution ─────────────

    private async Task<TenantResolveResult> ResolveTenantAsync(string? bodyTenantSlug, CancellationToken ct)
    {
        var host = Request.Host.Host;
        if (!IsTrustedHost(host))
        {
            return TenantResolveResult.Error(
                BadRequest(new ProblemDetails
                {
                    Title = "Untrusted host.",
                    Status = StatusCodes.Status400BadRequest,
                    Type = "host.untrusted",
                }));
        }

        var subdomain = ExtractSubdomain(host);
        var explicitSlug = string.IsNullOrWhiteSpace(bodyTenantSlug)
            ? null
            : bodyTenantSlug.Trim().ToLowerInvariant();

        string? candidate;
        if (subdomain is not null && explicitSlug is not null && !string.Equals(subdomain, explicitSlug, StringComparison.Ordinal))
        {
            return TenantResolveResult.Error(
                BadRequest(new ProblemDetails
                {
                    Title = "Conflicting tenant sources.",
                    Status = StatusCodes.Status400BadRequest,
                    Type = "tenant.source_conflict",
                }));
        }
        candidate = subdomain ?? explicitSlug;

        if (string.IsNullOrWhiteSpace(candidate))
        {
            return TenantResolveResult.Error(
                BadRequest(new ProblemDetails
                {
                    Title = "Tenant required.",
                    Status = StatusCodes.Status400BadRequest,
                    Type = "tenant.required",
                }));
        }

        if (ReservedSlugs.IsReserved(candidate))
        {
            // Same shape as invalid_credentials to avoid enumeration of
            // the reserved list.
            return TenantResolveResult.Error(
                Unauthorized(new ProblemDetails
                {
                    Title = "Invalid credentials.",
                    Status = StatusCodes.Status401Unauthorized,
                    Type = "auth.invalid_credentials",
                }));
        }

        var tenant = await _tenantCatalog.LookupBySlugAsync(candidate, ct).ConfigureAwait(false);
        if (tenant is null)
        {
            // R6 + ADV-004 — close the tenant-enumeration side channel
            // by returning the same shape login would have for any
            // wrong-user / wrong-password case.
            return TenantResolveResult.Error(
                Unauthorized(new ProblemDetails
                {
                    Title = "Invalid credentials.",
                    Status = StatusCodes.Status401Unauthorized,
                    Type = "auth.invalid_credentials",
                }));
        }

        _requestContext.Bind(tenant, HttpContext.TraceIdentifier, userId: null);
        return TenantResolveResult.Ok(candidate);
    }

    private bool IsTrustedHost(string host)
    {
        if (string.IsNullOrWhiteSpace(host))
        {
            return false;
        }
        foreach (var suffix in _authOptions.TrustedHostSuffixes)
        {
            if (host.Equals(suffix, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
            if (host.EndsWith("." + suffix, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }
        return false;
    }

    private static string? ExtractSubdomain(string host)
    {
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
        if (ReservedSlugs.IsReserved(candidate))
        {
            return null;
        }
        return candidate.Trim().ToLowerInvariant();
    }

    private bool TryReadAuthenticatedTenant(out string slug, out Guid userId)
    {
        slug = string.Empty;
        userId = Guid.Empty;
        var claims = User;
        var slugClaim = claims.FindFirst("tenant_slug")?.Value;
        var subClaim = claims.FindFirst(ClaimTypes.NameIdentifier)?.Value
                       ?? claims.FindFirst("sub")?.Value;
        if (string.IsNullOrWhiteSpace(slugClaim) || !Guid.TryParse(subClaim, out var parsed))
        {
            return false;
        }
        slug = slugClaim;
        userId = parsed;
        return true;
    }

    private readonly record struct TenantResolveResult(bool Success, string? Slug, IActionResult? ErrorResult)
    {
        public static TenantResolveResult Ok(string slug) => new(true, slug, null);

        public static TenantResolveResult Error(IActionResult error) => new(false, null, error);
    }
}

// ───────────── Wire DTOs (controller-local; mapped onto Application DTOs) ─────────────

public sealed record LoginRequestDto(
    string Email,
    string Password,
    bool RememberMe,
    string? TenantSlug);

public sealed record RefreshRequestDto(
    string RefreshToken,
    Guid UserId,
    string? TenantSlug);

public sealed record LogoutRequestDto(string RefreshToken, bool AllDevices);
