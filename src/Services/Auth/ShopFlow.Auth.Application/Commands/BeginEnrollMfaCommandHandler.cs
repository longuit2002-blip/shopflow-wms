using MediatR;
using ShopFlow.Auth.Application.Dtos;
using ShopFlow.Auth.Application.Ports;
using ShopFlow.SharedKernel.Domain;

namespace ShopFlow.Auth.Application.Commands;

/// <summary>
/// Sprint-9 U8 — start TOTP enrollment. Generates a fresh secret +
/// stashes in Redis with 10-min TTL + returns the otpauth:// URI for
/// QR rendering. Rejects with 409 when the user is already enrolled.
/// </summary>
public sealed class BeginEnrollMfaCommandHandler
    : IRequestHandler<BeginEnrollMfaCommand, Result<BeginEnrollMfaResponse>>
{
    private const string AlreadyEnrolled = "auth.mfa_already_enrolled";
    private const string InvalidCredentials = "auth.invalid_credentials";

    private readonly IUserRepository _users;
    private readonly ITotpProvider _totp;
    private readonly IEnrollmentSecretStore _enrollmentStore;
    private readonly TimeProvider _clock;

    public BeginEnrollMfaCommandHandler(
        IUserRepository users,
        ITotpProvider totp,
        IEnrollmentSecretStore enrollmentStore,
        TimeProvider clock)
    {
        _users = users;
        _totp = totp;
        _enrollmentStore = enrollmentStore;
        _clock = clock;
    }

    public async Task<Result<BeginEnrollMfaResponse>> Handle(BeginEnrollMfaCommand request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        var user = await _users.GetByIdAsync(request.UserId, ct).ConfigureAwait(false);
        if (user is null || !user.IsActive)
        {
            return Result<BeginEnrollMfaResponse>.Failure("Invalid credentials.", InvalidCredentials);
        }
        if (user.MfaEnrolled)
        {
            return Result<BeginEnrollMfaResponse>.Failure(
                "MFA already enrolled.",
                AlreadyEnrolled);
        }

        var secret = _totp.GenerateSecret();
        var enrollmentId = await _enrollmentStore
            .StoreAsync(request.TenantSlug, user.Id, secret, ct)
            .ConfigureAwait(false);
        var provisioningUri = _totp.GenerateProvisioningUri(secret, user.Email, "ShopFlow WMS");
        var secretBase32 = _totp.EncodeSecretBase32(secret);

        var expiresAt = _clock.GetUtcNow().UtcDateTime.AddMinutes(10);
        return Result<BeginEnrollMfaResponse>.Success(
            new BeginEnrollMfaResponse(enrollmentId, provisioningUri, secretBase32, expiresAt));
    }
}
