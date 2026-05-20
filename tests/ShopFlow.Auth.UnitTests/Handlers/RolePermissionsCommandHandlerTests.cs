using FluentAssertions;
using NSubstitute;
using ShopFlow.Auth.Application.Commands;
using ShopFlow.Auth.Application.Ports;
using ShopFlow.Auth.Domain;
using ShopFlow.SharedKernel.Authorization;
using ShopFlow.SharedKernel.Domain;
using Xunit;

namespace ShopFlow.Auth.UnitTests.Handlers;

/// <summary>
/// Sprint-9 U8 + U16 — pin the KTD13 OwnerCritical guard. Any operation
/// that would leave the Owner row missing one of
/// <see cref="PermissionKeys.OwnerCritical"/> must fail with
/// <c>auth.role_permissions_owner_critical_locked</c>.
/// </summary>
public sealed class RolePermissionsCommandHandlerTests
{
    private readonly IRolePermissionRepository _repo = Substitute.For<IRolePermissionRepository>();

    private RolePermissionsCommandHandler Build() => new(_repo);

    private void StubOwnerHasAllPermissions()
    {
        _repo.GetForRoleAsync(UserRole.Owner, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<string>>(PermissionKeys.All.ToList()));
        _repo.UpdateForRoleAsync(Arg.Any<UserRole>(), Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Result.Success()));
    }

    [Fact]
    public async Task RemovePermission_OwnerCriticalKey_Rejected()
    {
        StubOwnerHasAllPermissions();
        var actor = Guid.NewGuid();

        var result = await Build().Handle(
            new RolePermissionsCommand(
                actor,
                UserRole.Owner,
                RolePermissionsOperation.RemovePermission,
                PermissionKeys.AuthAdminUsersCreate,
                null,
                Guid.NewGuid()),
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be("auth.role_permissions_owner_critical_locked");
        await _repo.DidNotReceive().UpdateForRoleAsync(
            Arg.Any<UserRole>(), Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SetAll_OwnerWithoutCriticalKey_Rejected()
    {
        StubOwnerHasAllPermissions();
        var actor = Guid.NewGuid();
        // Subset that drops AuthAdminLockoutUnlock (an OwnerCritical key).
        var desired = PermissionKeys.All
            .Where(k => k != PermissionKeys.AuthAdminLockoutUnlock)
            .ToList();

        var result = await Build().Handle(
            new RolePermissionsCommand(
                actor,
                UserRole.Owner,
                RolePermissionsOperation.SetAll,
                null,
                desired,
                Guid.NewGuid()),
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be("auth.role_permissions_owner_critical_locked");
    }

    [Fact]
    public async Task RemovePermission_NonCriticalKey_FromOwner_Succeeds()
    {
        StubOwnerHasAllPermissions();
        var actor = Guid.NewGuid();

        var result = await Build().Handle(
            new RolePermissionsCommand(
                actor,
                UserRole.Owner,
                RolePermissionsOperation.RemovePermission,
                PermissionKeys.InventoryRead, // not in OwnerCritical
                null,
                Guid.NewGuid()),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task RemovePermission_FromPicker_DoesNotInvokeOwnerCriticalGuard()
    {
        _repo.GetForRoleAsync(UserRole.Picker, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<string>>(new[] { PermissionKeys.InventoryRead }));
        _repo.UpdateForRoleAsync(Arg.Any<UserRole>(), Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Result.Success()));
        var actor = Guid.NewGuid();

        var result = await Build().Handle(
            new RolePermissionsCommand(
                actor,
                UserRole.Picker,
                RolePermissionsOperation.RemovePermission,
                PermissionKeys.InventoryRead,
                null,
                Guid.NewGuid()),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue("OwnerCritical guard only fires when TargetRole == Owner");
    }

    [Fact]
    public async Task AddPermission_UnknownKey_Rejected()
    {
        StubOwnerHasAllPermissions();
        var actor = Guid.NewGuid();

        var result = await Build().Handle(
            new RolePermissionsCommand(
                actor,
                UserRole.Picker,
                RolePermissionsOperation.AddPermission,
                "not.a.real.permission",
                null,
                Guid.NewGuid()),
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be("auth.role_permissions_unknown_key");
    }

    [Fact]
    public async Task AddPermission_MissingPermissionKey_Rejected()
    {
        StubOwnerHasAllPermissions();
        var actor = Guid.NewGuid();

        var result = await Build().Handle(
            new RolePermissionsCommand(
                actor,
                UserRole.Picker,
                RolePermissionsOperation.AddPermission,
                null,
                null,
                Guid.NewGuid()),
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be("auth.role_permissions_operation_invalid");
    }
}
