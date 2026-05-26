using MediatR;
using ShopFlow.Auth.Application.Dtos;
using ShopFlow.SharedKernel.Domain;

namespace ShopFlow.Auth.Application.Commands;

/// <summary>
/// MediatR command for the end-user login flow (Sprint-8 U7 / F1).
/// </summary>
/// <remarks>
/// <para>The tenant slug is bound by the U9 controller via the
/// routing middleware (header / subdomain / claim), then passed in
/// explicitly. The handler does NOT resolve the slug itself — the
/// controller has already validated it against the host-suffix
/// allowlist and bound the per-request DbContext.</para>
///
/// <para>All credential failure modes — missing user, inactive user,
/// wrong password — collapse to the same error code
/// <c>auth.invalid_credentials</c> so the response shape leaks no
/// enumeration signal (R6).</para>
/// </remarks>
public sealed record LoginCommand(
    string Email,
    string Password,
    bool RememberMe,
    string TenantSlug,
    string SourceIp,
    string UserAgent,
    Guid CorrelationId
) : IRequest<Result<LoginResponse>>;
