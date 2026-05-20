using MediatR;
using ShopFlow.Auth.Application.Dtos;
using ShopFlow.SharedKernel.Domain;

namespace ShopFlow.Auth.Application.Commands;

/// <summary>
/// Sprint-9 R14 — regenerate the full set of 10 recovery codes for the
/// authenticated user (replaces existing codes). Handler in U8: delete
/// existing codes + generate 10 fresh + hash with Argon2-RecoveryCode
/// profile + persist + return plaintexts ONCE.
/// </summary>
public sealed record GenerateRecoveryCodesCommand(
    Guid UserId,
    string TenantSlug,
    Guid CorrelationId) : IRequest<Result<RecoveryCodeView>>;
