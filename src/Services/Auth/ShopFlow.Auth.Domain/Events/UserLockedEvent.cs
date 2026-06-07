using ShopFlow.SharedKernel.Domain;

namespace ShopFlow.Auth.Domain.Events;

/// <summary>
/// Raised by <see cref="Entities.User.RegisterFailedLogin"/> when the failed
/// attempt crosses the lockout threshold. Carries the lockout expiry so the
/// LoginCommandHandler can emit the cross-module <c>AccountLockedV1</c>
/// outbox event with the same boundary timestamp. Sprint-9 U1.
/// </summary>
/// <remarks>
/// Fires ONCE on the boundary attempt (e.g. the 5th failure inside the
/// sliding window), not on every subsequent failure while locked. Domain
/// event payload omits the source IP — the audit log + outbox event carry
/// that; the aggregate has no access to request context.
/// </remarks>
public sealed record UserLockedEvent(
    Guid UserId,
    int FailedLoginCount,
    DateTime LockedUntil,
    DateTime OccurredAt
) : IDomainEvent;
