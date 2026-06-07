using ShopFlow.SharedKernel.Domain;

namespace ShopFlow.Auth.Domain.Events;

/// <summary>
/// Raised by <see cref="User.Create"/> when a new tenant user is added.
/// Sprint-8 U1. Carried on the User aggregate's domain-event buffer; the
/// outbox interceptor drains it at <c>SaveChanges</c> time.
/// </summary>
/// <remarks>
/// Sprint-8 does NOT publish a cross-module integration event for this
/// (no other module consumes user creation). Stays a domain-event-only
/// signal until a cross-module need surfaces (e.g., Analytics user-activity
/// dashboards in Phase-2).
/// </remarks>
public sealed record UserCreatedEvent(Guid UserId, string Email, UserRole Role, DateTime OccurredAt)
    : IDomainEvent;
