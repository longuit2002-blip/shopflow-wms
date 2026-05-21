namespace ShopFlow.Notification.Domain.ValueObjects;

/// <summary>
/// Enumerates the four transactional-email kinds Sprint-9.5 sends — one
/// per Sprint-9 cross-module Auth event. Stored as a string in the
/// <c>notification_outbox</c> / <c>notification_log</c> /
/// <c>notification_dead_letter</c> tables; the CHECK constraint on each
/// table pins the persisted set so any future addition forces a
/// coordinated migration update (mirrors the
/// <see cref="ShopFlow.Auth.Domain.UserRole"/> + <c>chk_users_role</c>
/// pairing in Sprint-8 U3).
/// </summary>
public enum NotificationKind
{
    /// <summary>Backs <c>PasswordResetRequestedV1</c>.</summary>
    PasswordReset = 0,

    /// <summary>Backs <c>RefreshReuseDetectedV1</c> (Owner alert).</summary>
    RefreshReuse = 1,

    /// <summary>Backs <c>AccountLockedV1</c> (Owner alert).</summary>
    AccountLocked = 2,

    /// <summary>Backs <c>MfaEnrolledV1</c> (user confirmation).</summary>
    MfaEnrolled = 3,
}
