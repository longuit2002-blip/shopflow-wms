using ShopFlow.SharedKernel.Domain;

namespace ShopFlow.Auth.Domain.Events;

/// <summary>
/// Raised by <see cref="User.SetRole"/> when the user's role actually
/// changes (no-op assignments do not raise the event). Sprint-8 U1.
/// </summary>
/// <remarks>
/// Carries both <c>FromRole</c> and <c>ToRole</c> so audit trails and
/// future role-based-access analytics can reconstruct transitions without
/// joining historical snapshots.
/// </remarks>
public sealed record UserRoleChangedEvent(
    Guid UserId,
    UserRole FromRole,
    UserRole ToRole,
    DateTime OccurredAt
) : IDomainEvent;
