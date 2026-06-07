using ShopFlow.Auth.Domain;
using ShopFlow.Auth.Domain.Entities;
using ShopFlow.SharedKernel.Domain;

namespace ShopFlow.Auth.Application.Ports;

/// <summary>
/// Read + write surface for the per-tenant <c>users</c> table (Sprint-8
/// U3 ships the EF-backed impl; Sprint-9 U2 adds the role-fan-out shape
/// the Notification consumers need for Owner-alert email fan-out).
/// Email lookups are case-insensitive to match the
/// <c>ux_users_email_lower</c> UNIQUE index — callers don't have to
/// normalize at the call site.
/// </summary>
/// <remarks>
/// <para>Per ADR-0003 the table lives in the per-tenant database; tenant
/// context is bound to the scoped <c>AuthDbContext</c> via
/// <see cref="ShopFlow.SharedKernel.Application.IRequestContext"/>. The
/// repository never sees a <c>tenantId</c> parameter — the routing
/// middleware binds the request to the correct DB before this port is
/// called.</para>
/// </remarks>
public interface IUserRepository
{
    /// <summary>
    /// Look up by email (case-insensitive). Returns null when no row
    /// matches — the login handler maps null to the canonical
    /// <c>auth.invalid_credentials</c> error so missing-user and
    /// wrong-password responses are indistinguishable (R6 enumeration
    /// prevention).
    /// </summary>
    Task<User?> GetByEmailAsync(string email, CancellationToken ct);

    /// <summary>
    /// Look up by aggregate id. Returns null when no row matches.
    /// Used by ChangePassword + admin update flows where the access
    /// token already carried the user id claim.
    /// </summary>
    Task<User?> GetByIdAsync(Guid userId, CancellationToken ct);

    /// <summary>
    /// Insert a new user row. Returns the persisted aggregate on
    /// success; returns failure with <c>auth.email_in_use</c> when the
    /// UNIQUE-23505 violation fires (race between two admin invites,
    /// or retried provisioning).
    /// </summary>
    Task<Result<User>> AddAsync(User user, CancellationToken ct);

    /// <summary>
    /// Persist pending changes on the tracked aggregate (role
    /// transitions, password rotations, deactivations, lockout state,
    /// MFA toggles). EF tracks the aggregate from a prior
    /// <c>GetById</c>/<c>GetByEmail</c>; callers mutate via the named
    /// aggregate methods then call <c>UpdateAsync</c> to flush.
    /// </summary>
    Task UpdateAsync(User user, CancellationToken ct);

    /// <summary>
    /// Paged listing for admin surfaces. Ordering is <c>created_at
    /// DESC, id DESC</c> for stable pagination under concurrent
    /// inserts (mirrors Sprint-7.5 U6 cursor convention).
    /// <paramref name="page"/> is 1-based.
    /// </summary>
    Task<IReadOnlyList<User>> ListAsync(int page, int pageSize, CancellationToken ct);

    /// <summary>
    /// All users with the requested role. Used by Sprint-9
    /// Notification consumers for Owner-fan-out (chain-reuse alert +
    /// account-locked alert). Does NOT page — the Owner row count is
    /// expected to be small (typically 1-3 per tenant); for non-Owner
    /// roles callers should use <see cref="ListAsync"/> with a page.
    /// </summary>
    Task<IReadOnlyList<User>> ListByRoleAsync(UserRole role, CancellationToken ct);
}
