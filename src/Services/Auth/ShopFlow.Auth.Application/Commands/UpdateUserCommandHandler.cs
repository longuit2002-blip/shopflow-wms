using MediatR;
using ShopFlow.Auth.Application.Dtos;
using ShopFlow.Auth.Application.Ports;
using ShopFlow.Auth.Application.Services;
using ShopFlow.Auth.Domain;
using ShopFlow.SharedKernel.Domain;

namespace ShopFlow.Auth.Application.Commands;

/// <summary>
/// Sprint-8 U8 — consolidated admin update handler. Routes the three
/// admin operations (SetRole / ResetPassword / Deactivate) via the
/// <see cref="UpdateUserOperation"/> discriminator on the command
/// (KTD8). Reduces the handler-count footprint vs three separate
/// handlers without losing R14 / R15 / R16 coverage.
/// </summary>
/// <remarks>
/// <para>ResetPassword + Deactivate both cascade
/// <see cref="IRefreshTokenStore.RevokeAllForUserAsync"/> — fresh
/// credentials or deactivated user → no live sessions can survive.
/// SetRole does NOT revoke sessions; the existing access token's
/// role claim stays in flight for up to 15 min until next refresh
/// (acceptable for role-elevation use cases; deactivation is the
/// hard-stop path).</para>
/// </remarks>
public sealed class UpdateUserCommandHandler
    : IRequestHandler<UpdateUserCommand, Result<UpdateUserResult>>
{
    private const string UserNotFoundCode = "users.not_found";
    private const string RoleInvalidCode = "users.role_invalid";

    private readonly IUserRepository _users;
    private readonly IPasswordHasher _hasher;
    private readonly IPasswordGenerator _generator;
    private readonly IRefreshTokenStore _refreshStore;

    public UpdateUserCommandHandler(
        IUserRepository users,
        IPasswordHasher hasher,
        IPasswordGenerator generator,
        IRefreshTokenStore refreshStore)
    {
        _users = users;
        _hasher = hasher;
        _generator = generator;
        _refreshStore = refreshStore;
    }

    public async Task<Result<UpdateUserResult>> Handle(
        UpdateUserCommand request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        var user = await _users.GetByIdAsync(request.UserId, ct).ConfigureAwait(false);
        if (user is null)
        {
            return Result<UpdateUserResult>.Failure(
                $"User '{request.UserId}' not found.", UserNotFoundCode);
        }

        switch (request.Operation)
        {
            case UpdateUserOperation.SetRole:
                {
                    if (!Enum.TryParse<UserRole>(request.NewRole, ignoreCase: false, out var newRole)
                        || !Enum.IsDefined(newRole))
                    {
                        return Result<UpdateUserResult>.Failure(
                            $"Unknown role '{request.NewRole}'.", RoleInvalidCode);
                    }
                    user.SetRole(newRole);
                    await _users.UpdateAsync(user, ct).ConfigureAwait(false);
                    return Result<UpdateUserResult>.Success(
                        new UpdateUserResult(user.Id, ResetPassword: null));
                }

            case UpdateUserOperation.ResetPassword:
                {
                    var tempPwd = _generator.Generate();
                    user.UpdatePassword(_hasher.Hash(tempPwd));
                    await _users.UpdateAsync(user, ct).ConfigureAwait(false);
                    await _refreshStore
                        .RevokeAllForUserAsync(request.TenantSlug, user.Id, ct)
                        .ConfigureAwait(false);
                    return Result<UpdateUserResult>.Success(new UpdateUserResult(
                        user.Id,
                        ResetPassword: new ResetPasswordResponse(user.Id, tempPwd)));
                }

            case UpdateUserOperation.Deactivate:
                {
                    user.Deactivate();
                    await _users.UpdateAsync(user, ct).ConfigureAwait(false);
                    await _refreshStore
                        .RevokeAllForUserAsync(request.TenantSlug, user.Id, ct)
                        .ConfigureAwait(false);
                    return Result<UpdateUserResult>.Success(
                        new UpdateUserResult(user.Id, ResetPassword: null));
                }

            default:
                return Result<UpdateUserResult>.Failure(
                    $"Unsupported operation '{request.Operation}'.",
                    "users.operation_invalid");
        }
    }
}
