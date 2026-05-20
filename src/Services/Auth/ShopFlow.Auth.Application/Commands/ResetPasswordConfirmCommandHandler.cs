using System.Security.Cryptography;
using System.Text;
using MediatR;
using ShopFlow.Auth.Application.Ports;
using ShopFlow.SharedKernel.Domain;

namespace ShopFlow.Auth.Application.Commands;

/// <summary>
/// Sprint-9 U8 — reset-confirm handler. Consumes the token via
/// predicate-in-UPDATE (single-use atomic) + writes the new password
/// + revokes every refresh session for the user.
/// </summary>
public sealed class ResetPasswordConfirmCommandHandler : IRequestHandler<ResetPasswordConfirmCommand, Result>
{
    private const string InvalidCredentials = "auth.invalid_credentials";
    private const string PasswordTooShort = "auth.password_too_short";
    private const int MinPasswordLength = 8;

    private readonly IPasswordResetTokenRepository _resetTokens;
    private readonly IUserRepository _users;
    private readonly IPasswordHasher _hasher;
    private readonly IRefreshTokenStore _refreshStore;
    private readonly TimeProvider _clock;

    public ResetPasswordConfirmCommandHandler(
        IPasswordResetTokenRepository resetTokens,
        IUserRepository users,
        IPasswordHasher hasher,
        IRefreshTokenStore refreshStore,
        TimeProvider clock)
    {
        _resetTokens = resetTokens;
        _users = users;
        _hasher = hasher;
        _refreshStore = refreshStore;
        _clock = clock;
    }

    public async Task<Result> Handle(ResetPasswordConfirmCommand request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrWhiteSpace(request.NewPassword) || request.NewPassword.Length < MinPasswordLength)
        {
            return Result.Failure(
                $"New password must be at least {MinPasswordLength} characters.",
                PasswordTooShort);
        }

        if (string.IsNullOrWhiteSpace(request.Token))
        {
            return Result.Failure("Invalid credentials.", InvalidCredentials);
        }

        var tokenHash = SHA256.HashData(Encoding.UTF8.GetBytes(request.Token));
        var consume = await _resetTokens.TryConsumeAsync(tokenHash, _clock, ct).ConfigureAwait(false);
        if (!consume.IsSuccess)
        {
            return Result.Failure("Invalid credentials.", InvalidCredentials);
        }

        var user = await _users.GetByIdAsync(consume.Value, ct).ConfigureAwait(false);
        if (user is null || !user.IsActive)
        {
            return Result.Failure("Invalid credentials.", InvalidCredentials);
        }

        var newHash = _hasher.Hash(request.NewPassword);
        user.UpdatePassword(newHash);
        await _users.UpdateAsync(user, ct).ConfigureAwait(false);

        // R32 — fresh password means every existing session ends.
        await _refreshStore
            .RevokeAllForUserAsync(request.TenantSlug, user.Id, ct)
            .ConfigureAwait(false);

        return Result.Success();
    }
}
