using OtpNet;
using ShopFlow.Auth.Application.Ports;

namespace ShopFlow.Auth.Infrastructure.Mfa;

/// <summary>
/// Sprint-9 U4 Otp.NET-backed implementation of <see cref="ITotpProvider"/>.
/// Stateless — no per-user state lives on the instance. Singleton.
/// </summary>
/// <remarks>
/// <para>Drift window is ±1 step (<see cref="VerificationWindow.RfcSpecifiedNetworkDelay"/>),
/// matching RFC 6238 §5.2 guidance. The handler is responsible for
/// rejecting matched <c>timeStep</c> values that equal the user's
/// stored <c>last_used_step</c> (within-window replay guard).</para>
///
/// <para><see cref="VerifyOtp"/> never throws on malformed input; the
/// handler collapses all failure cases to
/// <c>auth.invalid_credentials</c> per R6.</para>
/// </remarks>
public sealed class OtpNetTotpProvider : ITotpProvider
{
    public byte[] GenerateSecret()
    {
        return KeyGeneration.GenerateRandomKey(20);
    }

    public string GenerateProvisioningUri(byte[] secret, string email, string issuer)
    {
        ArgumentNullException.ThrowIfNull(secret);
        ArgumentException.ThrowIfNullOrWhiteSpace(email);
        ArgumentException.ThrowIfNullOrWhiteSpace(issuer);

        // Otp.NET's OtpUri builder: issuer + account label + secret in
        // base32. Standard otpauth:// URI shape parseable by Google
        // Authenticator, 1Password, Authy, Bitwarden, etc. SHA1 / 6
        // digits / 30 sec period are the Otp.NET defaults.
        var encoded = Base32Encoding.ToString(secret);
        var builder = new OtpUri(
            OtpType.Totp,
            encoded,
            email,
            issuer,
            OtpHashMode.Sha1,
            digits: 6,
            period: 30);
        return builder.ToString();
    }

    public OtpVerificationResult VerifyOtp(byte[] secret, string code, TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        if (secret is null || secret.Length == 0 || string.IsNullOrWhiteSpace(code))
        {
            return new OtpVerificationResult(IsValid: false, TimeStep: 0);
        }

        try
        {
            var totp = new Totp(secret, step: 30, mode: OtpHashMode.Sha1, totpSize: 6);
            var now = clock.GetUtcNow().UtcDateTime;
            var valid = totp.VerifyTotp(
                now,
                code,
                out var matchedStep,
                VerificationWindow.RfcSpecifiedNetworkDelay);
            return new OtpVerificationResult(valid, matchedStep);
        }
        catch (Exception)
        {
            // Malformed input (non-numeric, wrong length, etc.) lands
            // here. Collapse to invalid — per R6, never differentiate
            // "wrong shape" from "wrong value" at the API surface.
            return new OtpVerificationResult(IsValid: false, TimeStep: 0);
        }
    }
}
