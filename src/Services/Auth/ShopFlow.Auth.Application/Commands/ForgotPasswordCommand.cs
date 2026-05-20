using MediatR;
using ShopFlow.SharedKernel.Domain;

namespace ShopFlow.Auth.Application.Commands;

/// <summary>
/// Sprint-9 R29-R32 — anonymous password-reset initiation. Handler in
/// U8: per-account cooldown + CSPRNG token + 30-min TTL + outbox emit
/// + always-200-generic-response (R6 enumeration discipline) + Argon2
/// synthetic-delay constant-time on unknown email.
/// </summary>
public sealed record ForgotPasswordCommand(
    string Email,
    string TenantSlug,
    string SourceIp,
    string UserAgent,
    Guid CorrelationId) : IRequest<Result>;
