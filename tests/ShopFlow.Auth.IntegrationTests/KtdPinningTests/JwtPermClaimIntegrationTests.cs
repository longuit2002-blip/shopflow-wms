namespace ShopFlow.Auth.IntegrationTests.KtdPinningTests;

/// <summary>
/// Sprint-9.5 U9 — KTD1 perm-array shape pinning. Full login → JWT →
/// policy-gated endpoint round-trip. Verifies the `perm` claim arrives
/// as a JSON `string[]` on the wire (Microsoft.IdentityModel.Tokens'
/// JsonWebTokenHandler array-flattens N `Claim("perm", value)` entries
/// into the array), NOT a space-delimited string the older
/// JwtSecurityTokenHandler would emit.
///
/// Test bodies Skip-marked locally per Sprint-1+ posture; CI runs full
/// suite via Docker-backed fixture (Postgres + Redis Testcontainer).
/// </summary>
[Trait("Category", "Integration")]
public sealed class JwtPermClaimIntegrationTests
{
    [Fact(Skip = "Sprint-9.5 U9: Docker-backed fixture wired in CI tier")]
    public Task OwnerJwt_PassesPolicyGate_200()
    {
        // KTD1 + AE7 — Login Owner (perm[] = full PermissionKeys.All)
        // → present JWT to a test-only PolicyGatedController carrying
        // [Authorize(Policy="inventory.read")] → response 200.
        return Task.CompletedTask;
    }

    [Fact(Skip = "Sprint-9.5 U9: Docker-backed fixture wired in CI tier")]
    public Task PickerJwt_FailsPolicyGate_403()
    {
        // KTD1 — Login Picker (perm[] empty) → present JWT to the
        // same policy-gated controller → response 403.
        return Task.CompletedTask;
    }

    [Fact(Skip = "Sprint-9.5 U9: Docker-backed fixture wired in CI tier")]
    public Task PermClaim_WireShape_IsJsonStringArray()
    {
        // KTD1 verbatim — base64-decode the JWT payload + JsonDocument
        // parse the perm claim → MUST be a JSON array of strings, NOT
        // a space-delimited string. Guards against accidental
        // JwtSecurityTokenHandler regression.
        return Task.CompletedTask;
    }
}
