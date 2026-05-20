using System.Text.RegularExpressions;
using ShopFlow.Auth.Domain.Events;
using ShopFlow.SharedKernel.Domain;

namespace ShopFlow.Auth.Domain.Entities;

/// <summary>
/// Authenticated tenant user — Sprint-8 U1.
/// </summary>
/// <remarks>
/// <para>Per ADR-0003 the aggregate lives in the per-tenant database; no
/// <c>tenant_id</c> field on the entity itself. Tenant context flows
/// through <see cref="ShopFlow.SharedKernel.Application.IRequestContext"/>
/// and the scoped <see cref="ShopFlow.Auth.Infrastructure.AuthDbContext"/>
/// in U3 (DB-per-tenant binding).</para>
///
/// <para>Validation lives on the factory; mutations go through named methods
/// that buffer domain events. The aggregate does NOT hash passwords — that's
/// the Application layer's <see cref="ShopFlow.Auth.Application.Ports.IPasswordHasher"/>
/// in U2/U4. The aggregate accepts a pre-hashed PHC string and stores it
/// verbatim. Plaintext passwords never live on the entity.</para>
/// </remarks>
public sealed class User : BaseEntity
{
    // RFC 5322 minimum sanity check; intentionally lax to accept the long tail
    // of valid B2B email shapes (subdomains, plus-addressing, tagged TLDs).
    private static readonly Regex EmailSanity = new(
        @"^[^@\s]+@[^@\s]+\.[^@\s]+$",
        RegexOptions.Compiled);

    private const int MaxEmailLength = 254;

    public string Email { get; private set; } = default!;

    public string PasswordHash { get; private set; } = default!;

    public UserRole Role { get; private set; }

    public bool IsActive { get; private set; }

    public DateTime? LastLoginAt { get; private set; }

    private User() { }

    /// <summary>
    /// Factory for a new tenant user. Validates email shape + non-empty
    /// password hash + role enum. Email is normalized to lowercase so the
    /// case-insensitive UNIQUE index in U3 holds.
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
    /// <see cref="UserRoleChangedEvent"/> with both from + to.
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

    /// <summary>
    /// Soft-delete: marks the user inactive. The row is retained for audit
    /// reference; login attempts return <c>auth.invalid_credentials</c> (R6
    /// indistinguishable-from-missing-user shape, per the enumeration-prevention
    /// design).
    /// </summary>
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
    /// Records a successful login. No domain event — auth-event observability
    /// flows through OpenTelemetry traces in Sprint-8; an <c>auth_audit_log</c>
    /// table is Sprint-9+ when audit UI surfaces.
    /// </summary>
    public void RecordLogin()
    {
        LastLoginAt = DateTime.UtcNow;
    }
}
