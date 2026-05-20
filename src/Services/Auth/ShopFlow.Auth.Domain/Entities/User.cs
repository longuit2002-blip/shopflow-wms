using System.Text.RegularExpressions;
using ShopFlow.Auth.Domain.Events;
using ShopFlow.SharedKernel.Domain;

namespace ShopFlow.Auth.Domain.Entities;

/// <summary>
/// Authenticated tenant user — Sprint-8 U1 / Sprint-9 U1.
/// </summary>
/// <remarks>
/// <para>Per ADR-0003 the aggregate lives in the per-tenant database; no
/// <c>tenant_id</c> field on the entity itself. Tenant context flows
/// through <see cref="ShopFlow.SharedKernel.Application.IRequestContext"/>
/// and the scoped <c>AuthDbContext</c> (DB-per-tenant binding).</para>
///
/// <para>Validation lives on the factory; mutations go through named methods
/// that buffer domain events. The aggregate does NOT hash passwords — that's
/// the Application layer's <c>IPasswordHasher</c>. The aggregate accepts a
/// pre-hashed PHC string and stores it verbatim. Plaintext passwords never
/// live on the entity.</para>
///
/// <para>Sprint-9 grafts the lockout + MFA state machine: <see cref="FailedLoginCount"/>,
/// <see cref="LockedUntil"/>, <see cref="LastFailedLoginAt"/> drive the
/// sliding-window lockout (5 fails / 15 min default; values come from
/// AuthOptions). <see cref="MfaRequired"/> + <see cref="MfaEnrolled"/> drive
/// the MFA branch in LoginCommandHandler (U8). The Owner role's
/// <c>mfa_required = true</c> invariant is enforced at handler + Domain
/// layer (R17 / KTD AdminMfaReset).</para>
/// </remarks>
public sealed class User : BaseEntity
{
    private static readonly Regex EmailSanity = new(
        @"^[^@\s]+@[^@\s]+\.[^@\s]+$",
        RegexOptions.Compiled);

    private const int MaxEmailLength = 254;

    public string Email { get; private set; } = default!;

    public string PasswordHash { get; private set; } = default!;

    public UserRole Role { get; private set; }

    public bool IsActive { get; private set; }

    public DateTime? LastLoginAt { get; private set; }

    // -------- Sprint-9 lockout columns --------

    /// <summary>Current failure count inside the sliding window.</summary>
    public int FailedLoginCount { get; private set; }

    /// <summary>UTC when the lockout expires; null means not locked.</summary>
    public DateTime? LockedUntil { get; private set; }

    /// <summary>
    /// UTC of the most recent failed login. Drives the sliding-window reset:
    /// when <c>now - LastFailedLoginAt &gt; window</c>, <see cref="RegisterFailedLogin"/>
    /// resets the counter before incrementing.
    /// </summary>
    /// <remarks>
    /// Deviation from plan U1 file list — the plan enumerated 4 new columns
    /// but the sliding-window test scenario ("16 min after 4 failures
    /// resets the counter") requires a fifth time field. Without it the
    /// window could only be reset by a successful login, which contradicts
    /// the test contract. Captured in the U1 commit body.
    /// </remarks>
    public DateTime? LastFailedLoginAt { get; private set; }

    // -------- Sprint-9 MFA columns --------

    /// <summary>True when this user is required to enroll TOTP (Owner = true by default; KTD R17 invariant).</summary>
    public bool MfaRequired { get; private set; }

    /// <summary>True when this user has completed TOTP enrollment.</summary>
    public bool MfaEnrolled { get; private set; }

    private User() { }

    /// <summary>
    /// Factory for a new tenant user. Validates email shape + non-empty
    /// password hash + role enum. Email is normalized to lowercase so the
    /// case-insensitive UNIQUE index holds. Owner-role users are minted
    /// with <see cref="MfaRequired"/>=true to support the Sprint-9 first-time
    /// forced enrollment flow (R17).
    /// </summary>
    public static User Create(string email, string passwordHash, UserRole role)
    {
        if (string.IsNullOrWhiteSpace(email))
        {
            throw new ArgumentException("Email is required.", nameof(email));
        }
        var normalized = email.Trim().ToLowerInvariant();
        if (normalized.Length > MaxEmailLength)
        {
            throw new ArgumentException(
                $"Email must be {MaxEmailLength} characters or fewer.",
                nameof(email));
        }
        if (!EmailSanity.IsMatch(normalized))
        {
            throw new ArgumentException("Email is malformed.", nameof(email));
        }
        if (string.IsNullOrWhiteSpace(passwordHash))
        {
            throw new ArgumentException(
                "Password hash is required (pre-hash via IPasswordHasher before calling Create).",
                nameof(passwordHash));
        }
        if (!Enum.IsDefined(role))
        {
            throw new ArgumentException("Role must be a defined UserRole value.", nameof(role));
        }

        var user = new User
        {
            Email = normalized,
            PasswordHash = passwordHash,
            Role = role,
            IsActive = true,
            MfaRequired = role == UserRole.Owner,
        };
        user.RaiseDomainEvent(new UserCreatedEvent(user.Id, user.Email, user.Role, user.CreatedAt));
        return user;
    }

    /// <summary>
    /// Update the stored password hash. Caller (Application layer) supplies
    /// the new PHC string after re-hashing the plaintext. Raises
    /// <see cref="UserPasswordChangedEvent"/> and bumps <c>UpdatedAt</c>.
    /// </summary>
    public void UpdatePassword(string newPasswordHash)
    {
        if (string.IsNullOrWhiteSpace(newPasswordHash))
        {
            throw new ArgumentException(
                "Password hash is required.",
                nameof(newPasswordHash));
        }
        PasswordHash = newPasswordHash;
        UpdatedAt = DateTime.UtcNow;
        RaiseDomainEvent(new UserPasswordChangedEvent(Id, UpdatedAt.Value));
    }

    /// <summary>
    /// Set the user's role. No-op when the requested role equals the current
    /// role — no event raised, no <c>UpdatedAt</c> bump. Otherwise raises
    /// <see cref="UserRoleChangedEvent"/> with both from + to. Role changes
    /// do NOT toggle <see cref="MfaRequired"/> — that's a separate decision
    /// surfaced through <see cref="RequireMfa"/>.
    /// </summary>
    public void SetRole(UserRole newRole)
    {
        if (!Enum.IsDefined(newRole))
        {
            throw new ArgumentException("Role must be a defined UserRole value.", nameof(newRole));
        }
        if (Role == newRole)
        {
            return;
        }
        var previous = Role;
        Role = newRole;
        UpdatedAt = DateTime.UtcNow;
        RaiseDomainEvent(new UserRoleChangedEvent(Id, previous, newRole, UpdatedAt.Value));
    }

    /// <summary>Soft-delete: marks the user inactive.</summary>
    public void Deactivate()
    {
        if (!IsActive)
        {
            return;
        }
        IsActive = false;
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>
    /// Records a successful login. Also resets the lockout counter so the
    /// sliding-window state restarts clean — a legitimate user back in good
    /// standing should not carry over yesterday's three failed attempts.
    /// </summary>
    public void RecordLogin()
    {
        LastLoginAt = DateTime.UtcNow;
        if (FailedLoginCount > 0 || LastFailedLoginAt is not null)
        {
            FailedLoginCount = 0;
            LastFailedLoginAt = null;
        }
    }

    // -------- Sprint-9 lockout state machine --------

    /// <summary>
    /// Records a failed login attempt and, if it crosses the threshold,
    /// flips the user to locked. Returns <c>true</c> on the boundary attempt
    /// so the caller can emit the cross-module <c>AccountLockedV1</c> event
    /// exactly once; subsequent failures while already locked return
    /// <c>false</c> and do NOT extend <see cref="LockedUntil"/>.
    /// </summary>
    /// <remarks>
    /// Sliding window: when <paramref name="clock"/>'s now exceeds
    /// <see cref="LastFailedLoginAt"/> + <paramref name="window"/>, the
    /// counter is reset to 1 before evaluating the threshold. <see cref="RaiseDomainEvent"/>
    /// fires <see cref="UserLockedEvent"/> on the boundary attempt.
    /// </remarks>
    /// <param name="clock">Time source — production binds <c>TimeProvider.System</c>; tests use FakeTimeProvider.</param>
    /// <param name="maxAttempts">Threshold (e.g. 5). Reaching this value triggers lockout.</param>
    /// <param name="window">Sliding window (e.g. 15 minutes). Reset triggers below this gap.</param>
    /// <param name="lockoutDuration">How long the lockout lasts (e.g. 15 minutes).</param>
    public bool RegisterFailedLogin(
        TimeProvider clock,
        int maxAttempts,
        TimeSpan window,
        TimeSpan lockoutDuration)
    {
        ArgumentNullException.ThrowIfNull(clock);
        if (maxAttempts <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxAttempts), "maxAttempts must be positive.");
        }
        if (window <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(window), "window must be positive.");
        }
        if (lockoutDuration <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(lockoutDuration), "lockoutDuration must be positive.");
        }

        var now = clock.GetUtcNow().UtcDateTime;

        // Already locked → caller's per-IP rate limit + 401-silent posture
        // covers the brute-force surface. We don't extend the lockout because
        // doing so would let the attacker indefinitely prolong the legitimate
        // user's logout. Test scenario "RegisterFailedLogin on an already-locked
        // user does NOT extend LockedUntil" pins this.
        if (LockedUntil is not null && LockedUntil.Value > now)
        {
            LastFailedLoginAt = now;
            UpdatedAt = now;
            return false;
        }

        // Sliding-window reset: if the previous failure was outside the window,
        // restart the count at 1 for this attempt.
        if (LastFailedLoginAt is not null && now - LastFailedLoginAt.Value > window)
        {
            FailedLoginCount = 0;
        }

        FailedLoginCount++;
        LastFailedLoginAt = now;
        UpdatedAt = now;

        if (FailedLoginCount >= maxAttempts)
        {
            LockedUntil = now + lockoutDuration;
            RaiseDomainEvent(new UserLockedEvent(Id, FailedLoginCount, LockedUntil.Value, now));
            return true;
        }

        return false;
    }

    /// <summary>
    /// Clear the failure counter without unlocking. Called on every successful
    /// authentication step (post-password-verify, post-MFA-verify) so a
    /// half-successful path doesn't leave stale failure state.
    /// </summary>
    public void ResetFailures()
    {
        if (FailedLoginCount == 0 && LastFailedLoginAt is null)
        {
            return;
        }
        FailedLoginCount = 0;
        LastFailedLoginAt = null;
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>
    /// Owner-only manual unlock surface. Clears the lockout AND the failure
    /// counter so the user is back to a clean slate. Idempotent.
    /// </summary>
    public void Unlock()
    {
        if (LockedUntil is null && FailedLoginCount == 0)
        {
            return;
        }
        LockedUntil = null;
        FailedLoginCount = 0;
        LastFailedLoginAt = null;
        UpdatedAt = DateTime.UtcNow;
    }

    // -------- Sprint-9 MFA state machine --------

    /// <summary>
    /// Sets the <see cref="MfaRequired"/> flag. The Application layer
    /// enforces the Owner-role invariant (R17): the AdminMfaReset handler
    /// rejects flipping this to <c>false</c> for an Owner user.
    /// </summary>
    public void RequireMfa(bool required)
    {
        if (MfaRequired == required)
        {
            return;
        }
        MfaRequired = required;
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>
    /// Marks the user as having completed TOTP enrollment. Raises
    /// <see cref="UserMfaEnrolledEvent"/>. The Application layer persists
    /// the encrypted secret + recovery codes in the same transaction.
    /// </summary>
    public void MarkMfaEnrolled()
    {
        if (MfaEnrolled)
        {
            return;
        }
        MfaEnrolled = true;
        UpdatedAt = DateTime.UtcNow;
        RaiseDomainEvent(new UserMfaEnrolledEvent(Id, UpdatedAt.Value));
    }

    /// <summary>
    /// Self-service MFA disable (the user re-verifies password and turns it
    /// off). Permitted only when <see cref="MfaRequired"/> is false — the
    /// Application handler enforces that gate. Raises
    /// <see cref="UserMfaDisabledEvent"/> with <c>ByOwnerAction = false</c>.
    /// </summary>
    public void MarkMfaDisabled()
    {
        if (!MfaEnrolled)
        {
            return;
        }
        MfaEnrolled = false;
        UpdatedAt = DateTime.UtcNow;
        RaiseDomainEvent(new UserMfaDisabledEvent(Id, ByOwnerAction: false, UpdatedAt.Value));
    }

    /// <summary>
    /// Owner-driven MFA reset for a target user. Clears <see cref="MfaEnrolled"/>
    /// so the user must re-enroll on next login. Raises
    /// <see cref="UserMfaDisabledEvent"/> with <c>ByOwnerAction = true</c>.
    /// </summary>
    public void MarkMfaReset()
    {
        if (!MfaEnrolled)
        {
            return;
        }
        MfaEnrolled = false;
        UpdatedAt = DateTime.UtcNow;
        RaiseDomainEvent(new UserMfaDisabledEvent(Id, ByOwnerAction: true, UpdatedAt.Value));
    }
}
