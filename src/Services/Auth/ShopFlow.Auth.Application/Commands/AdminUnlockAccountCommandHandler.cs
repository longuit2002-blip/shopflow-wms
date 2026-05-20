using MediatR;
using ShopFlow.Auth.Application.Ports;
using ShopFlow.SharedKernel.Domain;

namespace ShopFlow.Auth.Application.Commands;

/// <summary>
/// Sprint-9 U8 — Owner-driven manual unlock for a locked user. Clears
/// <c>locked_until</c> + <c>failed_login_count</c> + <c>last_failed_login_at</c>.
/// </summary>
public sealed class AdminUnlockAccountCommandHandler : IRequestHandler<AdminUnlockAccountCommand, Result>
{
    private const string TargetNotFound = "auth.target_not_found";

    private readonly IUserRepository _users;

    public AdminUnlockAccountCommandHandler(IUserRepository users)
    {
        _users = users;
    }

    public async Task<Result> Handle(AdminUnlockAccountCommand request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        var target = await _users.GetByIdAsync(request.TargetUserId, ct).ConfigureAwait(false);
        if (target is null)
        {
            return Result.Failure("Target user not found.", TargetNotFound);
        }

        target.Unlock();
        await _users.UpdateAsync(target, ct).ConfigureAwait(false);

        return Result.Success();
    }
}
