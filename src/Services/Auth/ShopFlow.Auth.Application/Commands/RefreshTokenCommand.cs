using MediatR;
using ShopFlow.Auth.Application.Dtos;
using ShopFlow.SharedKernel.Domain;

namespace ShopFlow.Auth.Application.Commands;

/// <summary>
/// MediatR command for refresh-token rotation (Sprint-8 U7 / F2).
/// </summary>
/// <remarks>
/// <para>The user id arrives in the request because the access token
/// itself has expired (or is about to) — the controller cannot rely
/// on <c>HttpContext.User</c> for sub-claim resolution. The refresh
/// store binds the rotation to <c>(tenantSlug, userId, presentedToken)</c>;
/// if the caller lies about <see cref="UserId"/>, the rotation will
/// not find a matching key and returns NotFound.</para>
/// </remarks>
public sealed record RefreshTokenCommand(
    string RefreshToken,
    Guid UserId,
    string TenantSlug,
    string SourceIp,
    string UserAgent,
    Guid CorrelationId) : IRequest<Result<RefreshResponse>>;
