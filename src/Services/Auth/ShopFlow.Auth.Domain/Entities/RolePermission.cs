namespace ShopFlow.Auth.Domain.Entities;

/// <summary>
/// Sprint-9 U3 — per-tenant RBAC row. Composite PK
/// <c>(role, permission_key)</c>; <see cref="PermissionKey"/> must be one
/// of <c>ShopFlow.SharedKernel.Authorization.PermissionKeys.All</c>.
/// Owner is seeded with every key by <c>RolePermissionsSeed</c> (U12);
/// Picker / Dispatcher start empty and accrete via the admin editor.
/// </summary>
public sealed class RolePermission
{
    public UserRole Role { get; private set; }

    public string PermissionKey { get; private set; } = default!;

    public DateTime CreatedAt { get; private set; }

    private RolePermission() { }

    public static RolePermission Grant(UserRole role, string permissionKey, DateTime now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(permissionKey);
        return new RolePermission
        {
            Role = role,
            PermissionKey = permissionKey,
            CreatedAt = now,
        };
    }
}
