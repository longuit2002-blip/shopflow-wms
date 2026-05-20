using ShopFlow.SharedKernel.Domain;

namespace ShopFlow.Auth.Domain.Events;

/// <summary>
/// Raised by <see cref="User.UpdatePassword"/> whenever the password hash
/// changes. Sprint-8 U1. Carries the user id only — never the plaintext
/// or the hash (the event traverses the outbox interceptor + may surface
/// in tracing, so credential material must not flow through it).
/// </summary>
public sealed record UserPasswordChangedEvent(Guid UserId, DateTime OccurredAt) : IDomainEvent;
