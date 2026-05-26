using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using ShopFlow.Auth.Application.Audit;
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
/// <c>auth.role_permissions_owner_critical_locked</c>. Sprint-12.5 U1
/// pins the <c>auth.role_permissions.changed</c> audit-row emit with
/// <c>added</c> + <c>removed</c> diff in metadata on the success path
/// and the NO-audit-on-rejection invariant.
/// </summary>
public sealed class RolePermissionsCommandHandlerTests
{
    private readonly IRolePermissionRepository _repo = Substitute.For<IRolePermissionRepository>();
    private readonly IAuthAuditLogRepository _auditLog = Substitute.For<IAuthAuditLogRepository>();

    private RolePermissionsCommandHandler Build() =>
        new(_repo, _auditLog, NullLogger<RolePermissionsCommandHandler>.Instance);

    private static RolePermissionsCommand Cmd(
        Guid actor,
        UserRole role,
        RolePermissionsOperation op,
        string? key,
        IReadOnlyList<string>? perms
    ) => new(actor, role, op, key, perms, "203.0.113.10", "test-ua/1.0", Guid.NewGuid());

    private void StubOwnerHasAllPermissions()
    {
        _repo
            .GetForRoleAsync(UserRole.Owner, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<string>>(PermissionKeys.All.ToList()));
        _repo
            .UpdateForRoleAsync(
                Arg.Any<UserRole>(),
                Arg.Any<IReadOnlyList<string>>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(Task.FromResult(Result.Success()));
    }

    [Fact]
    public async Task RemovePermission_OwnerCriticalKey_Rejected_NoAuditRow()
    {
        StubOwnerHasAllPermissions();
        var actor = Guid.NewGuid();

        var result = await Build()
            .Handle(
                Cmd(
                    actor,
                    UserRole.Owner,
                    RolePermissionsOperation.RemovePermission,
                    PermissionKeys.AuthAdminUsersCreate,
                    null
                ),
                CancellationToken.None
            );

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be("auth.role_permissions_owner_critical_locked");
        await _repo
            .DidNotReceive()
            .UpdateForRoleAsync(
                Arg.Any<UserRole>(),
                Arg.Any<IReadOnlyList<string>>(),
                Arg.Any<CancellationToken>()
            );
        await _auditLog
            .DidNotReceive()
            .AppendAsync(
                Arg.Any<string>(),
                Arg.Any<Guid?>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<Guid>(),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task SetAll_OwnerWithoutCriticalKey_Rejected()
    {
        StubOwnerHasAllPermissions();
        var actor = Guid.NewGuid();
        // Subset that drops AuthAdminLockoutUnlock (an OwnerCritical key).
        var desired = PermissionKeys
            .All.Where(k => k != PermissionKeys.AuthAdminLockoutUnlock)
            .ToList();

        var result = await Build()
            .Handle(
                Cmd(actor, UserRole.Owner, RolePermissionsOperation.SetAll, null, desired),
                CancellationToken.None
            );

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be("auth.role_permissions_owner_critical_locked");
    }

    [Fact]
    public async Task RemovePermission_NonCriticalKey_FromOwner_Succeeds_EmitsAuditWithRemovedDiff()
    {
        StubOwnerHasAllPermissions();
        var actor = Guid.NewGuid();

        var result = await Build()
            .Handle(
                Cmd(
                    actor,
                    UserRole.Owner,
                    RolePermissionsOperation.RemovePermission,
                    PermissionKeys.InventoryRead,
                    null
                ), // not in OwnerCritical
                CancellationToken.None
            );

        result.IsSuccess.Should().BeTrue();
        await _auditLog
            .Received(1)
            .AppendAsync(
                AuthAuditEventTypes.RolePermissionsChanged,
                actor,
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Is<string>(s =>
                    s.Contains("targetRole", StringComparison.Ordinal)
                    && s.Contains("Owner", StringComparison.Ordinal)
                    && s.Contains("removed", StringComparison.Ordinal)
                    && s.Contains(PermissionKeys.InventoryRead, StringComparison.Ordinal)
                ),
                Arg.Any<Guid>(),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task RemovePermission_FromPicker_DoesNotInvokeOwnerCriticalGuard_EmitsAudit()
    {
        _repo
            .GetForRoleAsync(UserRole.Picker, Arg.Any<CancellationToken>())
            .Returns(
                Task.FromResult<IReadOnlyList<string>>(new[] { PermissionKeys.InventoryRead })
            );
        _repo
            .UpdateForRoleAsync(
                Arg.Any<UserRole>(),
                Arg.Any<IReadOnlyList<string>>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(Task.FromResult(Result.Success()));
        var actor = Guid.NewGuid();

        var result = await Build()
            .Handle(
                Cmd(
                    actor,
                    UserRole.Picker,
                    RolePermissionsOperation.RemovePermission,
                    PermissionKeys.InventoryRead,
                    null
                ),
                CancellationToken.None
            );

        result.IsSuccess.Should().BeTrue("OwnerCritical guard only fires when TargetRole == Owner");
        await _auditLog
            .Received(1)
            .AppendAsync(
                AuthAuditEventTypes.RolePermissionsChanged,
                actor,
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<Guid>(),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task AddPermission_UnknownKey_Rejected_NoAuditRow()
    {
        StubOwnerHasAllPermissions();
        var actor = Guid.NewGuid();

        var result = await Build()
            .Handle(
                Cmd(
                    actor,
                    UserRole.Picker,
                    RolePermissionsOperation.AddPermission,
                    "not.a.real.permission",
                    null
                ),
                CancellationToken.None
            );

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be("auth.role_permissions_unknown_key");
        await _auditLog
            .DidNotReceive()
            .AppendAsync(
                Arg.Any<string>(),
                Arg.Any<Guid?>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<Guid>(),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task AddPermission_MissingPermissionKey_Rejected()
    {
        StubOwnerHasAllPermissions();
        var actor = Guid.NewGuid();

        var result = await Build()
            .Handle(
                Cmd(actor, UserRole.Picker, RolePermissionsOperation.AddPermission, null, null),
                CancellationToken.None
            );

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be("auth.role_permissions_operation_invalid");
    }

    [Fact]
    public async Task AddPermission_ToPicker_EmitsAuditWithAddedDiff()
    {
        _repo
            .GetForRoleAsync(UserRole.Picker, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<string>>(Array.Empty<string>()));
        _repo
            .UpdateForRoleAsync(
                Arg.Any<UserRole>(),
                Arg.Any<IReadOnlyList<string>>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(Task.FromResult(Result.Success()));
        var actor = Guid.NewGuid();

        var result = await Build()
            .Handle(
                Cmd(
                    actor,
                    UserRole.Picker,
                    RolePermissionsOperation.AddPermission,
                    PermissionKeys.InventoryRead,
                    null
                ),
                CancellationToken.None
            );

        result.IsSuccess.Should().BeTrue();
        await _auditLog
            .Received(1)
            .AppendAsync(
                AuthAuditEventTypes.RolePermissionsChanged,
                actor,
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Is<string>(s =>
                    s.Contains("added", StringComparison.Ordinal)
                    && s.Contains(PermissionKeys.InventoryRead, StringComparison.Ordinal)
                ),
                Arg.Any<Guid>(),
                Arg.Any<CancellationToken>()
            );
    }
}
