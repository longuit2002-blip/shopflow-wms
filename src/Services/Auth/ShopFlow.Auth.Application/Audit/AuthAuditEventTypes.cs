namespace ShopFlow.Auth.Application.Audit;

/// <summary>
/// Sprint-12.5 U1 — single source of truth for the 15 documented
/// <c>EventType</c> string constants written to <c>auth_audit_log</c>
/// via <see cref="ShopFlow.Auth.Application.Ports.IAuthAuditLogRepository"/>.
/// </summary>
/// <remarks>
/// <para>Sprint-9 ships the storage layer + DocBlock catalog on the
/// repository interface; Sprint-12.5 U1 wires the 12 command handlers
/// that map to one or more of these keys. Sprint-13+ may expand the
/// catalog to cover <c>BeginEnrollMfa</c> / <c>GenerateRecoveryCodes</c> /
/// <c>CreateUser</c> / <c>UpdateUser</c> handlers (KTD2).</para>
///
/// <para>Keep these as <c>const string</c> (not enum) so tests can
/// assert against the wire-shape string directly and the metadata
/// serializer doesn't have to ToString() an enum.</para>
/// </remarks>
public static class AuthAuditEventTypes
{
    public const string LoginSuccess = "auth.login.success";
    public const string LoginFailed = "auth.login.failed";
    public const string LoginLocked = "auth.login.locked";

    public const string RefreshSuccess = "auth.refresh.success";
    public const string RefreshReused = "auth.refresh.reused";

    public const string Logout = "auth.logout";

    public const string PasswordChanged = "auth.password.changed";
    public const string PasswordResetRequested = "auth.password.reset.requested";
    public const string PasswordResetCompleted = "auth.password.reset.completed";

    public const string MfaEnrolled = "auth.mfa.enrolled";
    public const string MfaUsed = "auth.mfa.used";
    public const string MfaDisabled = "auth.mfa.disabled";

    public const string MfaResetByOwner = "auth.mfa.reset_by_owner";
    public const string AccountUnlockedByOwner = "auth.account.unlocked_by_owner";

    public const string RolePermissionsChanged = "auth.role_permissions.changed";
}
