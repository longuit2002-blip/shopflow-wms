namespace ShopFlow.Auth.Application.Ports;

/// <summary>
/// Sprint-9 — short-lived signed token used to bridge the password-verify
/// step (returns 200 + token) and the second-factor verify step
/// (POST /api/auth/mfa/verify with the token). The intent claim
/// distinguishes the two flows: existing user with MFA enrolled
/// (Challenge) vs Owner-role user with forced MFA but not yet enrolled
/// (Enrollment).
/// </summary>
public interface IMfaChallengeTokenCodec
{
    /// <summary>
    /// Issue a signed, 5-minute TTL challenge token binding
    /// <paramref name="userId"/> + <paramref name="tenantSlug"/> +
    /// <paramref name="rememberMe"/> + <paramref name="intent"/>.
    /// </summary>
    string Issue(
        Guid userId,
        string tenantSlug,
        bool rememberMe,
        MfaChallengeIntent intent,
        DateTime issuedAt
    );

    /// <summary>
    /// Decode + validate the token. Returns null when the signature is
    /// invalid, the token is expired, or the payload is malformed
    /// (R6 — handler maps null to <c>auth.invalid_credentials</c>).
    /// </summary>
    MfaChallengePayload? TryDecode(string token, DateTime now);
}

public enum MfaChallengeIntent
{
    /// <summary>User has MFA enrolled — verify the OTP / recovery code.</summary>
    Challenge,

    /// <summary>User must enroll MFA before access (R10 / forced enrollment).</summary>
    Enrollment,
}

public sealed record MfaChallengePayload(
    Guid UserId,
    string TenantSlug,
    bool RememberMe,
    MfaChallengeIntent Intent
);
