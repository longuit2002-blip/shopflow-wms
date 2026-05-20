using MediatR;
using ShopFlow.Auth.Application.Dtos;
using ShopFlow.Auth.Application.Ports;
using ShopFlow.SharedKernel.Domain;

namespace ShopFlow.Auth.Application.Commands;

/// <summary>
/// Sprint-8 U7 — login handler. Composes
/// <see cref="IUserRepository"/> + <see cref="IPasswordHasher"/> +
/// <see cref="ITokenIssuer"/> + <see cref="IRefreshTokenStore"/>.
/// </summary>
/// <remarks>
/// <para>The single error code <c>auth.invalid_credentials</c>
/// covers every failure mode — missing user, inactive user, wrong
/// password. This is the enumeration-prevention discipline R6 calls
/// for: an attacker probing email addresses learns nothing from the
/// response shape about which mailboxes are registered.</para>
///
/// <para>Email format validation happens at the controller boundary
/// (U9 DTO validation); this handler trusts its input.</para>
/// </remarks>
public sealed class LoginCommandHandler : IRequestHandler<LoginCommand, Result<LoginResponse>>
{
    private const string InvalidCredentials = "auth.invalid_credentials";

    private readonly IUserRepository _users;
    private readonly IPasswordHasher _hasher;
    private readonly ITokenIssuer _issuer;
    private readonly IRefreshTokenStore _refreshStore;

    public LoginCommandHandler(
        IUserRepository users,
        IPasswordHasher hasher,
        ITokenIssuer issuer,
        IRefreshTokenStore refreshStore)
    {
        _users = users;
        _hasher = hasher;
        _issuer = issuer;
        _refreshStore = refreshStore;
    }

    public async Task<Result<LoginResponse>> Handle(LoginCommand request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrWhiteSpace(request.Password))
        {
            return Result<LoginResponse>.Failure("Invalid credentials.", InvalidCredentials);
        }

        var user = await _users.GetByEmailAsync(request.Email, ct).ConfigureAwait(false);
        if (user is null || !user.IsActive)
        {
            return Result<LoginResponse>.Failure("Invalid credentials.", InvalidCredentials);
        }

        if (!_hasher.Verify(request.Password, user.PasswordHash))
        {
            return Result<LoginResponse>.Failure("Invalid credentials.", InvalidCredentials);
        }

        // Stamp the login + persist via UpdateAsync. The aggregate
        // doesn't raise an event on RecordLogin (Sprint-9+ adds an
        // auth_audit_log table; today the path produces OTel traces
        // instead).
        user.RecordLogin();
        await _users.UpdateAsync(user, ct).ConfigureAwait(false);

        var accessToken = _issuer.IssueAccessToken(user, request.TenantSlug);
        var refreshToken = await _refreshStore
            .IssueAsync(request.TenantSlug, user.Id, request.RememberMe, ct)
            .ConfigureAwait(false);

        // The frontend reads RefreshTokenExpiresAt for the
        // session-expired-banner trigger (R13). Compute the same TTL
        // the store used so the response shape doesn't lie.
        var refreshExpiresAt = DateTime.UtcNow.AddDays(request.RememberMe ? 30 : 7);

        return Result<LoginResponse>.Success(new LoginResponse(
            AccessToken: accessToken.Jwt,
            AccessTokenExpiresAt: accessToken.ExpiresAt,
            RefreshToken: refreshToken,
            RefreshTokenExpiresAt: refreshExpiresAt,
            Role: user.Role.ToString(),
            Email: user.Email));
    }
}
