namespace ShopFlow.Auth.Application.Ports;

/// <summary>
/// Port for the RFC 6238 TOTP provider (Sprint-9 U4 ships the
/// Otp.NET-backed impl). The provider has no per-user state — every
/// call passes the user's raw shared secret as a byte array.
/// </summary>
/// <remarks>
/// <para>Drift window is ±1 step (RFC 6238 §5.2 +
/// <c>VerificationWindow.RfcSpecifiedNetworkDelay</c>) — covers the
/// typical client clock skew without widening the brute-force surface.</para>
///
/// <para><see cref="VerifyOtp"/> returns the matched <c>timeStep</c> so
/// the caller can persist it as <c>last_used_step</c> and reject
/// within-window replay attempts (handler responsibility, not the
/// provider's).</para>
/// </remarks>
public interface ITotpProvider
{
    /// <summary>
    /// Generate a fresh 20-byte (160-bit) cryptographically-random
    /// shared secret. Caller persists it via
    /// <see cref="ITotpSecretCipher.Encrypt"/> +
    /// <see cref="ITotpSecretRepository.UpsertAsync"/>.
    /// </summary>
    byte[] GenerateSecret();

    /// <summary>
    /// Build the <c>otpauth://totp/...</c> URI that authenticator apps
    /// (Google Authenticator, 1Password, Authy) parse into a TOTP
    /// account. <paramref name="email"/> is the account label;
    /// <paramref name="issuer"/> appears as the issuer string in the
    /// app. Both are URL-encoded by the impl.
    /// </summary>
    string GenerateProvisioningUri(byte[] secret, string email, string issuer);

    /// <summary>
    /// Verify a 6-digit OTP against the secret using a ±1-step drift
    /// window. <paramref name="clock"/> drives the time reference so
    /// tests can pin a specific window via <c>FakeTimeProvider</c>.
    /// Returns <c>false</c> + <c>timeStep = 0</c> on any failure
    /// (malformed code, mismatch, expired window).
    /// </summary>
    OtpVerificationResult VerifyOtp(byte[] secret, string code, TimeProvider clock);

    /// <summary>
    /// Encode the raw secret as base32 for manual-entry display in the
    /// enrollment screen (the QR is the otpauth URI; users without a
    /// camera need the secret as text).
    /// </summary>
    string EncodeSecretBase32(byte[] secret);
}

/// <summary>
/// Result envelope for <see cref="ITotpProvider.VerifyOtp"/>.
/// <see cref="TimeStep"/> is non-zero only on success.
/// </summary>
public sealed record OtpVerificationResult(bool IsValid, long TimeStep);
