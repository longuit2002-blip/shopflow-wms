using System.Reflection;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.Routing;
using ShopFlow.Auth.Api.Controllers;
using ShopFlow.SharedKernel.Authorization;
using Xunit;

namespace ShopFlow.Auth.UnitTests.Authorization;

/// <summary>
/// Sprint-10 U4 — reflection-based attribute-coverage pin for
/// <see cref="AuthAdminController"/>. Sprint-9 catalogued the 24
/// permission keys in <see cref="PermissionKeys.All"/> and registered
/// one ASP.NET Core policy per key; Sprint-10 U4 makes those policies
/// the canonical backend gate by adding
/// <c>[Authorize(Policy = PermissionKeys.X)]</c> per action.
/// </summary>
/// <remarks>
/// <para>Per KTD1 + KTD2, each action carrying an
/// <see cref="HttpMethodAttribute"/> must also carry exactly one
/// <see cref="AuthorizeAttribute"/> whose <c>Policy</c> equals the
/// canonical <see cref="PermissionKeys"/> constant (constants used
/// directly, not stringly-typed). Per KTD5 the set of AuthAdmin policy
/// names must equal <see cref="PermissionKeys.OwnerCritical"/> as a
/// set — a future drift in either direction (an AuthAdmin key removed
/// from OwnerCritical, or a new admin action whose key isn't in
/// OwnerCritical) fails the dual-pin test.</para>
///
/// <para>Origin AE2 is covered by the structural pin: the class-level
/// <c>[Authorize(Roles = "Owner")]</c> attribute MUST be absent.</para>
/// </remarks>
public sealed class AuthAdminAuthorizePolicyCoverageTests
{
    private static IEnumerable<MethodInfo> ActionMethods() =>
        typeof(AuthAdminController)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(m => m.GetCustomAttributes().OfType<HttpMethodAttribute>().Any());

    private static string? PolicyOn(string methodName)
    {
        var method = typeof(AuthAdminController).GetMethod(
            methodName,
            BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly
        );
        method
            .Should()
            .NotBeNull(
                $"AuthAdminController must declare a public instance method named '{methodName}'"
            );

        var authorize = method!
            .GetCustomAttributes<AuthorizeAttribute>(inherit: false)
            .SingleOrDefault();
        authorize
            .Should()
            .NotBeNull($"action '{methodName}' must carry exactly one [Authorize] attribute");
        return authorize!.Policy;
    }

    [Fact]
    public void CreateUser_Carries_AuthAdminUsersCreatePolicy()
    {
        PolicyOn(nameof(AuthAdminController.CreateUser))
            .Should()
            .Be(PermissionKeys.AuthAdminUsersCreate);
    }

    [Fact]
    public void ListUsers_Carries_AuthAdminUsersListPolicy()
    {
        PolicyOn(nameof(AuthAdminController.ListUsers))
            .Should()
            .Be(PermissionKeys.AuthAdminUsersList);
    }

    [Fact]
    public void SetRole_Carries_AuthAdminUsersUpdateRolePolicy()
    {
        PolicyOn(nameof(AuthAdminController.SetRole))
            .Should()
            .Be(PermissionKeys.AuthAdminUsersUpdateRole);
    }

    [Fact]
    public void ResetPassword_Carries_AuthAdminUsersResetPasswordPolicy()
    {
        PolicyOn(nameof(AuthAdminController.ResetPassword))
            .Should()
            .Be(PermissionKeys.AuthAdminUsersResetPassword);
    }

    [Fact]
    public void Deactivate_Carries_AuthAdminUsersDeactivatePolicy()
    {
        PolicyOn(nameof(AuthAdminController.Deactivate))
            .Should()
            .Be(PermissionKeys.AuthAdminUsersDeactivate);
    }

    [Fact]
    public void AdminMfaReset_Carries_AuthAdminMfaResetPolicy()
    {
        PolicyOn(nameof(AuthAdminController.AdminMfaReset))
            .Should()
            .Be(PermissionKeys.AuthAdminMfaReset);
    }

    [Fact]
    public void AdminUnlock_Carries_AuthAdminLockoutUnlockPolicy()
    {
        PolicyOn(nameof(AuthAdminController.AdminUnlock))
            .Should()
            .Be(PermissionKeys.AuthAdminLockoutUnlock);
    }

    [Fact]
    public void GetRolePermissions_Carries_AuthAdminRolePermissionsReadPolicy()
    {
        PolicyOn(nameof(AuthAdminController.GetRolePermissions))
            .Should()
            .Be(PermissionKeys.AuthAdminRolePermissionsRead);
    }

    [Fact]
    public void UpdateRolePermissions_Carries_AuthAdminRolePermissionsUpdatePolicy()
    {
        PolicyOn(nameof(AuthAdminController.UpdateRolePermissions))
            .Should()
            .Be(PermissionKeys.AuthAdminRolePermissionsUpdate);
    }

    [Fact]
    public void EveryHttpAction_Carries_AuthorizeAttribute_WithKnownPolicy()
    {
        var actions = ActionMethods().ToList();

        actions.Should().NotBeEmpty("AuthAdminController must expose at least one HTTP action");

        foreach (var action in actions)
        {
            var authorize = action
                .GetCustomAttributes<AuthorizeAttribute>(inherit: false)
                .SingleOrDefault();

            authorize
                .Should()
                .NotBeNull(
                    $"action '{action.Name}' must carry exactly one [Authorize] attribute (Sprint-10 U4)"
                );
            authorize!
                .Policy.Should()
                .NotBeNullOrWhiteSpace($"action '{action.Name}' must specify a Policy name");
            PermissionKeys
                .All.Should()
                .Contain(
                    authorize.Policy!,
                    $"policy '{authorize.Policy}' on action '{action.Name}' must live in PermissionKeys.All"
                );
        }
    }

    [Fact]
    public void ClassLevel_AuthorizeAttribute_IsAbsent_AfterSprint10U4()
    {
        // Origin AE2 — Sprint-10 U4 retired the class-level
        // [Authorize(Roles = "Owner")] in favor of per-action policies.
        var classAuthorize = typeof(AuthAdminController).GetCustomAttribute<AuthorizeAttribute>(
            inherit: false
        );

        classAuthorize
            .Should()
            .BeNull(
                "AuthAdminController must NOT carry a class-level [Authorize] attribute — per-action policies own gating"
            );

        var classAuthorizeAll = typeof(AuthAdminController).GetCustomAttributes<AuthorizeAttribute>(
            inherit: false
        );
        classAuthorizeAll
            .Any(a => !string.IsNullOrWhiteSpace(a.Roles))
            .Should()
            .BeFalse("class-level role-gating (e.g. [Authorize(Roles=\"Owner\")]) must be removed");
    }

    [Fact]
    public void AuthAdminPolicySet_Equals_OwnerCritical_AsSet()
    {
        // KTD5 dual-pin — the 9 AuthAdmin action policies must equal
        // PermissionKeys.OwnerCritical as a set. A new admin action with
        // a key outside OwnerCritical, or an OwnerCritical entry that no
        // longer maps to an admin action, breaks this test loudly.
        var policiesOnActions = ActionMethods()
            .Select(m => m.GetCustomAttributes<AuthorizeAttribute>(inherit: false).Single().Policy!)
            .ToHashSet(StringComparer.Ordinal);

        var ownerCritical = PermissionKeys.OwnerCritical.ToHashSet(StringComparer.Ordinal);

        policiesOnActions
            .Should()
            .BeEquivalentTo(
                ownerCritical,
                "Sprint-10 U4 KTD5: AuthAdminController action policies must be exactly PermissionKeys.OwnerCritical"
            );
    }

    // Illustrative negative-path shape — kept Skip'd as documentation. A
    // full negative-path harness would build a TestServer, issue a JWT
    // missing the required perm claim, hit the endpoint, and assert 403.
    // That belongs in an integration suite, not a unit test.
    [Fact(
        Skip = "Negative-path coverage belongs in Auth.IntegrationTests against a real authorization pipeline."
    )]
    public void Action_RejectsCaller_WithoutMatchingPermClaim()
    {
        // Arrange: build TestServer, issue JWT with role=Owner but no
        //          perm=auth.admin.users.create claim.
        // Act:     POST /api/auth/admin/users with valid body.
        // Assert:  HTTP 403 ProblemDetails, action body never executes.
    }
}
