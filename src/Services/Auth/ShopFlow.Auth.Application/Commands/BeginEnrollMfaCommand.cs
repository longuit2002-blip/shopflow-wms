using MediatR;
using ShopFlow.Auth.Application.Dtos;
using ShopFlow.SharedKernel.Domain;

namespace ShopFlow.Auth.Application.Commands;

/// <summary>
/// Sprint-9 R12 — start TOTP enrollment for the authenticated user.
/// Handler in U8: generate 20-byte secret + store in Redis with
/// 10-min TTL + return provisioning URI + enrollment_id. Pre-flight
/// rejects with 409 <c>auth.mfa_already_enrolled</c> when the user
/// already has a row in <c>user_totp_secrets</c>.
/// </summary>
public sealed record BeginEnrollMfaCommand(Guid UserId, string TenantSlug)
    : IRequest<Result<BeginEnrollMfaResponse>>;
