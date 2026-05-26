using ShopFlow.SharedKernel.Domain;

namespace ShopFlow.Auth.Domain.Events;

/// <summary>
/// Raised by <see cref="Entities.User.MarkMfaDisabled"/> (self-service) and
/// <see cref="Entities.User.MarkMfaReset"/> (Owner action). Sprint-9 U1.
/// </summary>
/// <param name="ByOwnerAction">
/// True when the disable was driven by an Owner via the admin MFA-reset
/// surface; false for the user's own self-service disable. The audit log
/// reads this to distinguish the two cases for compliance review.
/// </param>
public sealed record UserMfaDisabledEvent(Guid UserId, bool ByOwnerAction, DateTime OccurredAt)
    : IDomainEvent;
