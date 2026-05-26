using MediatR;
using ShopFlow.Auth.Domain;
using ShopFlow.SharedKernel.Domain;

namespace ShopFlow.Auth.Application.Commands;

/// <summary>
/// Sprint-9 R3 — Owner-only edit of <c>role_permissions</c>. Mirrors
/// Sprint-8's discriminated <c>UpdateUserCommand</c> shape: the
/// <see cref="Operation"/> tag routes the handler to add / remove /
/// replace-all. The handler enforces KTD13's OwnerCritical guard —
/// any operation that would leave Owner row missing one of
/// <c>PermissionKeys.OwnerCritical</c> returns 422
/// <c>auth.role_permissions_owner_critical_locked</c>.
/// </summary>
public sealed record RolePermissionsCommand(
    Guid ActorUserId,
    UserRole TargetRole,
    RolePermissionsOperation Operation,
    string? PermissionKey,
    IReadOnlyList<string>? Permissions,
    string SourceIp,
    string UserAgent,
    Guid CorrelationId) : IRequest<Result>;

/// <summary>
/// Discriminator for <see cref="RolePermissionsCommand"/>.
/// </summary>
public enum RolePermissionsOperation
{
    /// <summary>
    /// Add a single permission to the target role's grant list.
    /// Requires <see cref="RolePermissionsCommand.PermissionKey"/>.
    /// </summary>
    AddPermission,

    /// <summary>
    /// Remove a single permission from the target role's grant list.
    /// Requires <see cref="RolePermissionsCommand.PermissionKey"/>.
    /// </summary>
    RemovePermission,

    /// <summary>
    /// Replace the target role's full grant list. Requires
    /// <see cref="RolePermissionsCommand.Permissions"/>.
    /// </summary>
    SetAll,
}
