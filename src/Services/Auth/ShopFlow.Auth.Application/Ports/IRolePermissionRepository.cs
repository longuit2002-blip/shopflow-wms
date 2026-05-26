using ShopFlow.Auth.Domain;
using ShopFlow.SharedKernel.Domain;

namespace ShopFlow.Auth.Application.Ports;

/// <summary>
/// Persistence port for <c>role_permissions</c> (Sprint-9 U3 ships the
/// EF impl). One composite PK <c>(role, permission_key)</c> per
/// granted permission. The Owner row is seeded with every
/// <see cref="ShopFlow.SharedKernel.Authorization.PermissionKeys.All"/>
/// entry at tenant-provision time (U12); Picker and Dispatcher start
/// empty and are populated through the Owner admin surface.
/// </summary>
/// <remarks>
/// <see cref="UpdateForRoleAsync"/> is a set-based replace: callers
/// pass the new full permission list and the impl diffs it against
/// the current rows. The handler in U8 enforces the
/// <see cref="ShopFlow.SharedKernel.Authorization.PermissionKeys.OwnerCritical"/>
/// guard before invoking the port (KTD13).
/// </remarks>
public interface IRolePermissionRepository
{
    /// <summary>
    /// All permission keys currently granted to the requested role.
    /// Returned in arbitrary order; callers sort if needed. Empty list
    /// when the role has no grants.
    /// </summary>
    Task<IReadOnlyList<string>> GetForRoleAsync(UserRole role, CancellationToken ct);

    /// <summary>
    /// Snapshot of every role's permission list (used by the admin
    /// editor + the cross-tenant pin test in U16).
    /// </summary>
    Task<IReadOnlyDictionary<UserRole, IReadOnlyList<string>>> ListAllAsync(CancellationToken ct);

    /// <summary>
    /// Replace the role's full permission set with the supplied list.
    /// Unknown keys (not in <see cref="ShopFlow.SharedKernel.Authorization.PermissionKeys.All"/>)
    /// are rejected with code <c>auth.role_permissions_unknown_key</c>.
    /// </summary>
    Task<Result> UpdateForRoleAsync(
        UserRole role,
        IReadOnlyList<string> permissionKeys,
        CancellationToken ct
    );
}
