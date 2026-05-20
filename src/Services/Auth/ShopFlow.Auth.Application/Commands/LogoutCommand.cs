using MediatR;
using ShopFlow.SharedKernel.Domain;

namespace ShopFlow.Auth.Application.Commands;

/// <summary>
/// MediatR command for logout (Sprint-8 U7 / F4). The access token
/// claims supply <see cref="UserId"/> + <see cref="TenantSlug"/>; the
/// body carries the refresh token to revoke. <see cref="AllDevices"/>
/// switches to the user-wide revoke-all cascade (R14).
/// </summary>
public sealed record LogoutCommand(
    string RefreshToken,
    bool AllDevices,
    Guid UserId,
    string TenantSlug) : IRequest<Result>;
