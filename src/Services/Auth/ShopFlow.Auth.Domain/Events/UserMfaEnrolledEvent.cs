using ShopFlow.SharedKernel.Domain;

namespace ShopFlow.Auth.Domain.Events;

/// <summary>
/// Raised by <see cref="Entities.User.MarkMfaEnrolled"/> when the user
/// completes TOTP enrollment. Sprint-9 U1. The infrastructure path persists
/// the encrypted secret + recovery codes alongside the aggregate save;
/// this event is observable for audit + Notification fan-out via the
/// cross-module <c>MfaEnrolledV1</c> contract.
/// </summary>
public sealed record UserMfaEnrolledEvent(Guid UserId, DateTime OccurredAt) : IDomainEvent;
