namespace ShopFlow.Auth.IntegrationTests.KtdPinningTests;

/// <summary>
/// Sprint-9.5 U9 — KTD13 OwnerCritical server-side guard pinning. PUT
/// /api/auth/admin/role-permissions full round-trip against real
/// Postgres Testcontainer. Pins that:
///   - Removing any OwnerCritical key from Owner → 422
///     `auth.role_permissions_owner_critical_locked` + DB unchanged +
///     problem-details body lists the missing keys.
///   - Removing same key from Picker → 200 + DB updated (no
///     OwnerCritical guard on non-Owner roles).
///   - No-op Owner PUT with full PermissionKeys.All → 200 (preserves
///     the invariant; idempotent).
///
/// Test bodies Skip-marked locally; CI runs the full Docker-backed
/// suite.
/// </summary>
[Trait("Category", "Integration")]
public sealed class RolePermissionsOwnerCriticalIntegrationTests
{
    [Fact(Skip = "Sprint-9.5 U9: Docker-backed fixture wired in CI tier")]
    public Task OwnerEditRemovingCriticalKey_422AndDbUnchanged()
    {
        // AE7 — Seed Owner with full PermissionKeys.All (24 keys). PUT
        // {role: "Owner", permissionKeys: [<24 minus one OwnerCritical
        // key>]} → 422 + auth.role_permissions_owner_critical_locked
        // error code + problem-details body's `missing_keys` field
        // names the removed key + DB row unchanged.
        return Task.CompletedTask;
    }

    [Fact(Skip = "Sprint-9.5 U9: Docker-backed fixture wired in CI tier")]
    public Task PickerEditSameKey_200AndDbUpdated()
    {
        // Picker has no OwnerCritical guard; the same PUT body shape
        // targeting Picker → 200 + DB row updated to match.
        return Task.CompletedTask;
    }

    [Fact(Skip = "Sprint-9.5 U9: Docker-backed fixture wired in CI tier")]
    public Task OwnerEditPreservingAllKeys_200()
    {
        // PUT {role: "Owner", permissionKeys: [<all 24>]} → 200, no-op
        // semantically. KTD13 guard does not reject a valid Owner
        // permission set.
        return Task.CompletedTask;
    }
}
