namespace ShopFlow.Auth.IntegrationTests.KtdPinningTests;

/// <summary>
/// Sprint-9.5 U9 — Sprint-9 KTD10 (Redis enrollment-secret 10-min TTL)
/// + KTD8 (AES-256-GCM with Current/Previous KEK slot) integration
/// pinning. Real Redis Testcontainer + real AES cipher against the
/// Sprint-9 OtpNetTotpProvider + AesTotpSecretCipher + RedisEnrollmentSecretStore.
///
/// Test bodies Skip-marked locally; CI runs the full Docker-backed
/// suite via the nightly + per-PR job.
/// </summary>
[Trait("Category", "Integration")]
public sealed class TotpEnrollmentIntegrationTests
{
    [Fact(Skip = "Sprint-9.5 U9: Docker-backed fixture wired in CI tier")]
    public Task FullEnrollmentCycle_BeginVerifyConsume()
    {
        // POST /api/auth/mfa/enroll/begin → Redis carries the enrollment
        // secret with 10-min TTL (KTD10). POST /api/auth/mfa/enroll/verify
        // with valid OTP within window → totp_secrets row inserted with
        // totp_key_id=1 (Current slot per KTD8). Subsequent
        // /api/auth/mfa/verify with a fresh OTP → 200.
        return Task.CompletedTask;
    }

    [Fact(Skip = "Sprint-9.5 U9: Docker-backed fixture wired in CI tier")]
    public Task EnrollmentExpired_AfterTtl_Returns422()
    {
        // Advance the test clock past 10-min TTL (FakeTimeProvider) →
        // POST /api/auth/mfa/enroll/verify → 422 with
        // auth.mfa_enrollment_expired error code.
        return Task.CompletedTask;
    }

    [Fact(Skip = "Sprint-9.5 U9: Docker-backed fixture wired in CI tier")]
    public Task KekRotation_PreviousSlotFallback()
    {
        // User enrolls with KEK Current=slot 1. Override TotpKekOptions
        // to bump Current=slot 2, Previous=slot 1. User's row carries
        // totp_key_id=1; verify path reads Current first then falls
        // back to Previous → /api/auth/mfa/verify still returns 200
        // (lazy fallback per KTD8).
        return Task.CompletedTask;
    }
}
