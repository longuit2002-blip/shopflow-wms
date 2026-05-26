using MediatR;
using ShopFlow.Auth.Application.Dtos;
using ShopFlow.SharedKernel.Domain;

namespace ShopFlow.Auth.Application.Commands;

/// <summary>
/// Discriminated admin command consolidating SetRole / ResetPassword /
/// Deactivate (KTD8 — single endpoint, operation-tag dispatch).
/// Sprint-8 U8 / R14 + R15 + R16.
/// </summary>
public sealed record UpdateUserCommand(
    Guid UserId,
    UpdateUserOperation Operation,
    string? NewRole,
    string TenantSlug
) : IRequest<Result<UpdateUserResult>>;

/// <summary>
/// Operation tag for <see cref="UpdateUserCommand"/>. The handler
/// switches on this to route to the correct aggregate-mutation branch.
/// </summary>
public enum UpdateUserOperation
{
    SetRole,
    ResetPassword,
    Deactivate,
}

/// <summary>
/// Union-shaped outcome carried by <see cref="UpdateUserCommand"/>.
/// SetRole + Deactivate populate <see cref="UserId"/> only; ResetPassword
/// populates <see cref="ResetPassword"/> with the freshly-generated
/// temporary password (same one-time-display + OTel redaction
/// discipline as <see cref="CreateUserResponse"/>).
/// </summary>
public sealed record UpdateUserResult(Guid UserId, ResetPasswordResponse? ResetPassword);
