using System.Reflection;

namespace ShopFlow.SharedKernel.Authorization;

/// <summary>
/// Sprint-9 U1 — static catalog of every named permission the policy engine
/// recognises. The <c>perm</c> claim on every access token carries a JSON
/// array of these keys; <c>RequireClaim("perm", &lt;key&gt;)</c> matches one
/// at a time per KTD1.
/// </summary>
/// <remarks>
/// <para><see cref="All"/> reflects over the public-static-const string
/// fields on this type — one source of truth, no separate registry. The
/// reflection cost is paid once at app boot when policy registration loops
/// over the list (KTD4).</para>
///
/// <para><see cref="OwnerCritical"/> is the subset the Owner role can never
/// shed. <c>RolePermissionsCommandHandler</c> (Sprint-9 U8) rejects any
/// edit that would leave the Owner row missing any of these (KTD13).</para>
/// </remarks>
public static class PermissionKeys
{
    // -------- Auth admin --------
    public const string AuthAdminUsersList = "auth.admin.users.list";
    public const string AuthAdminUsersCreate = "auth.admin.users.create";
    public const string AuthAdminUsersUpdateRole = "auth.admin.users.update-role";
    public const string AuthAdminUsersResetPassword = "auth.admin.users.reset-password";
    public const string AuthAdminUsersDeactivate = "auth.admin.users.deactivate";
    public const string AuthAdminLockoutUnlock = "auth.admin.lockout.unlock";
    public const string AuthAdminMfaReset = "auth.admin.mfa-reset";
    public const string AuthAdminRolePermissionsRead = "auth.admin.role-permissions.read";
    public const string AuthAdminRolePermissionsUpdate = "auth.admin.role-permissions.update";

    // -------- Inventory --------
    public const string InventoryRead = "inventory.read";
    public const string InventoryAdjust = "inventory.adjust";
    public const string InventorySkusWrite = "inventory.skus.write";
    public const string InventorySkusFlashSaleWrite = "inventory.skus.flash-sale.write";
    public const string InventorySkusThresholdWrite = "inventory.skus.threshold.write";

    // -------- Outbound (orders) --------
    public const string OutboundOrdersRead = "outbound.orders.read";
    public const string OutboundOrdersWrite = "outbound.orders.write";
    public const string OutboundOrdersPickConfirm = "outbound.orders.pick-confirm";
    public const string OutboundOrdersPackConfirm = "outbound.orders.pack-confirm";
    public const string OutboundOrdersShipConfirm = "outbound.orders.ship-confirm";
    public const string OutboundOrdersCancel = "outbound.orders.cancel";

    // -------- Inbound --------
    public const string InboundPosRead = "inbound.pos.read";
    public const string InboundPosWrite = "inbound.pos.write";
    public const string InboundReceiveConfirm = "inbound.receive.confirm";

    // -------- Hub --------
    public const string HubConnect = "hub.connect";

    /// <summary>
    /// Keys the Owner role can never lose. KTD13 server-side guard reads
    /// this list when validating <c>role_permissions</c> edits.
    /// </summary>
    public static readonly IReadOnlyList<string> OwnerCritical = new[]
    {
        AuthAdminUsersList,
        AuthAdminUsersCreate,
        AuthAdminUsersUpdateRole,
        AuthAdminUsersResetPassword,
        AuthAdminUsersDeactivate,
        AuthAdminLockoutUnlock,
        AuthAdminMfaReset,
        AuthAdminRolePermissionsRead,
        AuthAdminRolePermissionsUpdate,
    };

    /// <summary>
    /// Every public-static-const string field on this type. Used by
    /// <c>AddShopFlowPermissionPolicies</c> (Sprint-9 U7) to register one
    /// ASP.NET policy per key and by <c>RolePermissionsSeed</c> (U12) to
    /// seed the Owner row at tenant provision time.
    /// </summary>
    public static readonly IReadOnlyList<string> All = typeof(PermissionKeys)
        .GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy)
        .Where(f => f.IsLiteral && !f.IsInitOnly && f.FieldType == typeof(string))
        .Select(f => (string)f.GetRawConstantValue()!)
        .ToList()
        .AsReadOnly();
}
