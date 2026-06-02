using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Npgsql;
using ShopFlow.Auth.Application.Commands;
using ShopFlow.Migrate.Provisioning;
using ShopFlow.SharedKernel.Authorization;
using ShopFlow.TestSupport;
using Xunit;

namespace ShopFlow.Auth.IntegrationTests;

/// <summary>
/// Finish-line U5 — the tenant-isolation hard-problem proof (AE3 /
/// origin R32). Provisions two real tenant DBs (tenant-a + tenant-b)
/// behind one Auth.Api WAF via <see cref="MultiTenantAuthFixture"/> and
/// asserts no data crosses the per-tenant DB boundary: the live
/// <c>TenantRoutingMiddleware</c> + per-request DbContext binding is the
/// PDPA hard-isolation guarantee, exercised through the real request
/// pipeline rather than asserted at the repository layer.
///
/// <para>These were five empty <c>Task.CompletedTask</c> stubs behind a
/// permanent Skip until U5 (CLAUDE.md named a <c>MultiTenantAuthFixture</c>
/// that was never built). Now real, gated by <see cref="ProofFactAttribute"/>:
/// run via <c>task proofs</c> locally or automatically in CI.</para>
/// </summary>
[Collection(MultiTenantAuthCollection.Name)]
[Trait("Category", "Integration")]
public sealed class AuthCrossTenantTests
{
    private readonly MultiTenantAuthFixture _fixture;

    public AuthCrossTenantTests(MultiTenantAuthFixture fixture)
    {
        _fixture = fixture;
    }

    /// <summary>R32a — a request whose only tenant signal is the JWT's
    /// <c>tenant_slug=tenant-a</c> claim resolves to tenant-a's DB and
    /// succeeds; the listing surfaces tenant-a's own Owner.</summary>
    [ProofFact]
    public async Task SameTenantAligned_ListUsers_Returns200_WithOwnOwner()
    {
        var client = ClientForTenant(_fixture.TenantA.Slug);

        var response = await client.GetAsync("/api/auth/admin/users");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var emails = await ReadUserEmailsAsync(response);
        emails.Should().Contain(_fixture.TenantA.OwnerEmail);
    }

    /// <summary>R32b — a tenant-a JWT pointed at tenant-b via the routing
    /// header is a conflicting tenant signal; <c>TenantRoutingMiddleware</c>
    /// rejects it (403) before any tenant-b data can be touched. Authorization
    /// runs first and PASSES (full perm[]), so the rejection is purely the
    /// tenant boundary — never 200.</summary>
    [ProofFact]
    public async Task CrossTenantSignalConflict_ListUsers_IsRejected_Never200()
    {
        var client = ClientForTenant(_fixture.TenantA.Slug);
        // Force tenant-b via the live routing header while the JWT claims
        // tenant-a → header (tenant-b) ≠ jwt (tenant-a) → conflict.
        client.DefaultRequestHeaders.Add("X-ShopFlow-Tenant", _fixture.TenantB.Slug);

        var response = await client.GetAsync("/api/auth/admin/users");

        response.StatusCode.Should().NotBe(HttpStatusCode.OK);
        response.StatusCode.Should().BeOneOf(HttpStatusCode.Forbidden, HttpStatusCode.Unauthorized);
    }

    /// <summary>R32c — editing tenant-a's Picker role-permissions does not
    /// touch tenant-b's <c>role_permissions</c>. Verified by a direct DB read
    /// against tenant-b's connection after a successful tenant-a edit.</summary>
    [ProofFact]
    public async Task RolePermissionsEdit_OnTenantA_DoesNotLeakToTenantB()
    {
        var client = ClientForTenant(_fixture.TenantA.Slug);

        // Add a key that is NOT in the Picker baseline and is NOT
        // owner-critical (so the OwnerCritical guard can't reject it).
        var body = new
        {
            role = "Picker",
            operation = nameof(RolePermissionsOperation.AddPermission),
            permissionKey = PermissionKeys.OutboundOrdersShipConfirm,
            permissions = (string[]?)null,
        };

        var response = await client.PutAsJsonAsync("/api/auth/admin/role-permissions", body);
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // tenant-a's Picker now carries the extra key …
        var tenantAPicker = await ReadPickerKeysAsync(_fixture.TenantA.ConnectionString);
        tenantAPicker.Should().Contain(PermissionKeys.OutboundOrdersShipConfirm);

        // … but tenant-b's Picker is untouched (still exactly the baseline).
        var tenantBPicker = await ReadPickerKeysAsync(_fixture.TenantB.ConnectionString);
        tenantBPicker.Should().NotContain(PermissionKeys.OutboundOrdersShipConfirm);
        tenantBPicker.Should().BeEquivalentTo(RolePermissionsSeed.PickerBaseline);
    }

    /// <summary>R32d — an admin user-list scoped to tenant-a never surfaces
    /// tenant-b's distinct Owner row.</summary>
    [ProofFact]
    public async Task UserList_OnTenantA_ExcludesTenantBUsers()
    {
        var client = ClientForTenant(_fixture.TenantA.Slug);

        var response = await client.GetAsync("/api/auth/admin/users");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var emails = await ReadUserEmailsAsync(response);
        emails.Should().Contain(_fixture.TenantA.OwnerEmail);
        emails.Should().NotContain(_fixture.TenantB.OwnerEmail);
    }

    /// <summary>R32e — an admin MFA reset issued in tenant-a context against a
    /// userId that belongs to tenant-b resolves no user in tenant-a's DbContext
    /// → 404. The user-id resolution scope is correctly per-tenant; a foreign
    /// id cannot be reset across the boundary.</summary>
    [ProofFact]
    public async Task MfaReset_TargetingForeignTenantUser_Returns404()
    {
        var client = ClientForTenant(_fixture.TenantA.Slug);

        var foreignUserId = _fixture.TenantB.OwnerUserId;
        var response = await client.PostAsync(
            $"/api/auth/admin/users/{foreignUserId}/mfa/reset",
            content: null
        );

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ── helpers ──────────────────────────────────────────────────────────

    /// <summary>A client whose JWT carries the full perm[] (so authorization
    /// always passes — the only variable under test is the tenant boundary)
    /// and <c>tenant_slug=&lt;slug&gt;</c> (the routing signal). The JWT
    /// subject is the tenant's seeded Owner id, so actor-id reads resolve.</summary>
    private HttpClient ClientForTenant(string slug)
    {
        var ownerId =
            slug == _fixture.TenantA.Slug
                ? _fixture.TenantA.OwnerUserId
                : _fixture.TenantB.OwnerUserId;
        var ownerEmail =
            slug == _fixture.TenantA.Slug
                ? _fixture.TenantA.OwnerEmail
                : _fixture.TenantB.OwnerEmail;

        var jwt = _fixture.JwtBuilder.Build(
            tenantSlug: slug,
            userId: ownerId,
            includeKeys: PermissionKeys.All,
            email: ownerEmail,
            role: "Owner"
        );

        var client = _fixture.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", jwt);
        return client;
    }

    private static async Task<IReadOnlyList<string>> ReadUserEmailsAsync(
        HttpResponseMessage response
    )
    {
        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        var users = doc.RootElement.GetProperty("users");
        var emails = new List<string>();
        foreach (var u in users.EnumerateArray())
        {
            emails.Add(u.GetProperty("email").GetString()!);
        }
        return emails;
    }

    private static async Task<IReadOnlyList<string>> ReadPickerKeysAsync(string connStr)
    {
        var keys = new List<string>();
        await using var conn = new NpgsqlConnection(connStr);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT permission_key FROM role_permissions WHERE role = 'Picker';";
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            keys.Add(reader.GetString(0));
        }
        return keys;
    }
}
