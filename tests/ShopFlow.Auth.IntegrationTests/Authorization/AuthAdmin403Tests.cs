using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using ShopFlow.SharedKernel.Authorization;
using Xunit;

namespace ShopFlow.Auth.IntegrationTests.Authorization;

/// <summary>
/// Sprint-10.5 U4 — 403 wire-shape pinning for the 9 AuthAdmin actions
/// Sprint-10 U4 attached per-action <c>[Authorize(Policy = PermissionKeys.X)]</c>
/// to. Each fact submits a JWT whose <c>perm[]</c> omits the action's
/// required key (and includes the other 23 of 24); the per-action policy
/// engine rejects on authorization, ASP.NET Core returns 403.
///
/// <para>Endpoint mapping (verified against the live
/// <c>AuthAdminController</c> — doc-review flagged DELETE for
/// Deactivate + the nested-user paths for mfa/reset + unlock):</para>
/// <list type="bullet">
///   <item><c>POST /api/auth/admin/users</c> → omit <c>AuthAdminUsersCreate</c></item>
///   <item><c>GET /api/auth/admin/users</c> → omit <c>AuthAdminUsersList</c></item>
///   <item><c>PUT /api/auth/admin/users/{id}/role</c> → omit <c>AuthAdminUsersUpdateRole</c></item>
///   <item><c>POST /api/auth/admin/users/{id}/reset-password</c> → omit <c>AuthAdminUsersResetPassword</c></item>
///   <item><c>DELETE /api/auth/admin/users/{id}</c> → omit <c>AuthAdminUsersDeactivate</c></item>
///   <item><c>POST /api/auth/admin/users/{id}/mfa/reset</c> → omit <c>AuthAdminMfaReset</c></item>
///   <item><c>POST /api/auth/admin/users/{id}/unlock</c> → omit <c>AuthAdminLockoutUnlock</c></item>
///   <item><c>GET /api/auth/admin/role-permissions</c> → omit <c>AuthAdminRolePermissionsRead</c></item>
///   <item><c>PUT /api/auth/admin/role-permissions</c> → omit <c>AuthAdminRolePermissionsUpdate</c></item>
/// </list>
///
/// <para>Skip-marked locally per Sprint-1+ posture.</para>
/// </summary>
[Collection(AuthAdminAuthorizationCollection.Name)]
[Trait("Category", "Integration")]
public sealed class AuthAdmin403Tests
{
    private const string SkipReason =
        "Sprint-10.5 U4: Docker-backed fixture wired in CI tier; dev machine has no Docker daemon";

    private static readonly Guid UserIdStub = Guid.Parse("33333333-3333-3333-3333-333333333333");

    private readonly AuthAdminAuthorizationFixture _fixture;

    public AuthAdmin403Tests(AuthAdminAuthorizationFixture fixture)
    {
        _fixture = fixture;
    }

    // ── POST /api/auth/admin/users ────────────────────────────────────

    [Fact(Skip = SkipReason)]
    public async Task CreateUser_RejectsJwtMissingAuthAdminUsersCreate_With403()
    {
        using var client = BuildClientNarrowedFor(PermissionKeys.AuthAdminUsersCreate);
        var body = new { email = "new@shopflow.local", role = "Picker" };
        var response = await client.PostAsJsonAsync("/api/auth/admin/users", body);
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    // ── GET /api/auth/admin/users ─────────────────────────────────────

    [Fact(Skip = SkipReason)]
    public async Task ListUsers_RejectsJwtMissingAuthAdminUsersList_With403()
    {
        using var client = BuildClientNarrowedFor(PermissionKeys.AuthAdminUsersList);
        var response = await client.GetAsync("/api/auth/admin/users");
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    // ── PUT /api/auth/admin/users/{id}/role ──────────────────────────

    [Fact(Skip = SkipReason)]
    public async Task SetRole_RejectsJwtMissingAuthAdminUsersUpdateRole_With403()
    {
        using var client = BuildClientNarrowedFor(PermissionKeys.AuthAdminUsersUpdateRole);
        var body = new { newRole = "Picker" };
        var response = await client.PutAsJsonAsync(
            $"/api/auth/admin/users/{UserIdStub}/role",
            body
        );
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    // ── POST /api/auth/admin/users/{id}/reset-password ───────────────

    [Fact(Skip = SkipReason)]
    public async Task ResetPassword_RejectsJwtMissingAuthAdminUsersResetPassword_With403()
    {
        using var client = BuildClientNarrowedFor(PermissionKeys.AuthAdminUsersResetPassword);
        var response = await client.PostAsync(
            $"/api/auth/admin/users/{UserIdStub}/reset-password",
            content: null
        );
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    // ── DELETE /api/auth/admin/users/{id} ────────────────────────────

    [Fact(Skip = SkipReason)]
    public async Task Deactivate_RejectsJwtMissingAuthAdminUsersDeactivate_With403()
    {
        // Doc-review verification: HTTP verb is DELETE (not PUT).
        using var client = BuildClientNarrowedFor(PermissionKeys.AuthAdminUsersDeactivate);
        var response = await client.DeleteAsync($"/api/auth/admin/users/{UserIdStub}");
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    // ── POST /api/auth/admin/users/{id}/mfa/reset ────────────────────

    [Fact(Skip = SkipReason)]
    public async Task AdminMfaReset_RejectsJwtMissingAuthAdminMfaReset_With403()
    {
        // Doc-review verification: nested under /users/{id}/mfa/reset
        // (NOT /api/auth/admin/mfa-reset).
        using var client = BuildClientNarrowedFor(PermissionKeys.AuthAdminMfaReset);
        var response = await client.PostAsync(
            $"/api/auth/admin/users/{UserIdStub}/mfa/reset",
            content: null
        );
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    // ── POST /api/auth/admin/users/{id}/unlock ───────────────────────

    [Fact(Skip = SkipReason)]
    public async Task AdminUnlock_RejectsJwtMissingAuthAdminLockoutUnlock_With403()
    {
        // Doc-review verification: nested under /users/{id}/unlock
        // (NOT /api/auth/admin/unlock).
        using var client = BuildClientNarrowedFor(PermissionKeys.AuthAdminLockoutUnlock);
        var response = await client.PostAsync(
            $"/api/auth/admin/users/{UserIdStub}/unlock",
            content: null
        );
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    // ── GET /api/auth/admin/role-permissions ─────────────────────────

    [Fact(Skip = SkipReason)]
    public async Task GetRolePermissions_RejectsJwtMissingAuthAdminRolePermissionsRead_With403()
    {
        using var client = BuildClientNarrowedFor(PermissionKeys.AuthAdminRolePermissionsRead);
        var response = await client.GetAsync("/api/auth/admin/role-permissions");
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    // ── PUT /api/auth/admin/role-permissions ─────────────────────────

    [Fact(Skip = SkipReason)]
    public async Task UpdateRolePermissions_RejectsJwtMissingAuthAdminRolePermissionsUpdate_With403()
    {
        using var client = BuildClientNarrowedFor(PermissionKeys.AuthAdminRolePermissionsUpdate);
        var body = new
        {
            role = "Picker",
            operation = "Replace",
            permissionKey = (string?)null,
            permissions = Array.Empty<string>(),
        };
        var response = await client.PutAsJsonAsync("/api/auth/admin/role-permissions", body);
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    /// <summary>
    /// Build an HttpClient whose Authorization header carries a JWT with
    /// every key in <see cref="PermissionKeys.All"/> EXCEPT
    /// <paramref name="omittedKey"/>.
    /// </summary>
    private HttpClient BuildClientNarrowedFor(string omittedKey)
    {
        var includeKeys = PermissionKeys.All
            .Where(k => !string.Equals(k, omittedKey, StringComparison.Ordinal))
            .ToArray();
        var jwt = _fixture.JwtBuilder.Build(
            tenantSlug: AuthAdminAuthorizationFixture.TenantSlug,
            userId: Guid.NewGuid(),
            includeKeys: includeKeys
        );
        var client = _fixture.HttpClient;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", jwt);
        return client;
    }
}
