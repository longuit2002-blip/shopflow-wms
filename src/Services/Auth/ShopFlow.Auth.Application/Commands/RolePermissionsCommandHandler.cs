using MediatR;
using Microsoft.Extensions.Logging;
using ShopFlow.Auth.Application.Audit;
using ShopFlow.Auth.Application.Ports;
using ShopFlow.Auth.Domain;
using ShopFlow.SharedKernel.Authorization;
using ShopFlow.SharedKernel.Domain;

namespace ShopFlow.Auth.Application.Commands;

/// <summary>
/// Sprint-9 U8 — Owner-only RBAC editor. KTD13 — server-side guard
/// rejects any edit that would leave the Owner row missing one of
/// <see cref="PermissionKeys.OwnerCritical"/> with 422
/// <c>auth.role_permissions_owner_critical_locked</c>. Sprint-12.5 U1 —
/// emits <c>auth.role_permissions.changed</c> on the successful update
/// path with the actual <c>added</c> + <c>removed</c> diff (NOT the
/// full desired set) so the audit trail captures the operator's intent
/// over time. Rejections (OwnerCritical / unknown-key / operation-invalid)
/// do not emit — audit captures successful actions.
/// </summary>
public sealed class RolePermissionsCommandHandler : IRequestHandler<RolePermissionsCommand, Result>
{
    private const string OwnerCriticalLocked = "auth.role_permissions_owner_critical_locked";
    private const string UnknownKey = "auth.role_permissions_unknown_key";
    private const string OperationInvalid = "auth.role_permissions_operation_invalid";

    private readonly IRolePermissionRepository _repo;
    private readonly IAuthAuditLogRepository _auditLog;
    private readonly ILogger<RolePermissionsCommandHandler> _logger;

    public RolePermissionsCommandHandler(
        IRolePermissionRepository repo,
        IAuthAuditLogRepository auditLog,
        ILogger<RolePermissionsCommandHandler> logger
    )
    {
        _repo = repo;
        _auditLog = auditLog;
        _logger = logger;
    }

    public async Task<Result> Handle(RolePermissionsCommand request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        var current = await _repo.GetForRoleAsync(request.TargetRole, ct).ConfigureAwait(false);
        var allKnown = PermissionKeys.All.ToHashSet(StringComparer.Ordinal);

        List<string> desired;
        switch (request.Operation)
        {
            case RolePermissionsOperation.AddPermission:
                if (string.IsNullOrWhiteSpace(request.PermissionKey))
                {
                    return Result.Failure("PermissionKey required.", OperationInvalid);
                }
                if (!allKnown.Contains(request.PermissionKey))
                {
                    return Result.Failure(
                        $"Unknown permission key: {request.PermissionKey}",
                        UnknownKey
                    );
                }
                desired = current.Append(request.PermissionKey).Distinct().ToList();
                break;

            case RolePermissionsOperation.RemovePermission:
                if (string.IsNullOrWhiteSpace(request.PermissionKey))
                {
                    return Result.Failure("PermissionKey required.", OperationInvalid);
                }
                desired = current.Where(p => p != request.PermissionKey).ToList();
                break;

            case RolePermissionsOperation.SetAll:
                if (request.Permissions is null)
                {
                    return Result.Failure("Permissions list required.", OperationInvalid);
                }
                desired = request.Permissions.ToList();
                break;

            default:
                return Result.Failure("Operation invalid.", OperationInvalid);
        }

        // KTD13 — OwnerCritical guard. Only applies when the target IS Owner.
        if (request.TargetRole == UserRole.Owner)
        {
            var desiredSet = desired.ToHashSet(StringComparer.Ordinal);
            foreach (var critical in PermissionKeys.OwnerCritical)
            {
                if (!desiredSet.Contains(critical))
                {
                    return Result.Failure(
                        $"Owner role cannot lose critical permission '{critical}'.",
                        OwnerCriticalLocked
                    );
                }
            }
        }

        var update = await _repo
            .UpdateForRoleAsync(request.TargetRole, desired, ct)
            .ConfigureAwait(false);
        if (!update.IsSuccess)
        {
            return Result.Failure(update.Error!, update.ErrorCode);
        }

        // Sprint-12.5 U1 — capture the diff, not the full desired set,
        // so the audit row is small + immediately actionable. Use sets
        // for symmetric-difference semantics (no double-counting on
        // SetAll equivalents of an Add+Remove).
        var currentSet = current.ToHashSet(StringComparer.Ordinal);
        var desiredFinalSet = desired.ToHashSet(StringComparer.Ordinal);
        var added = desiredFinalSet
            .Except(currentSet)
            .OrderBy(s => s, StringComparer.Ordinal)
            .ToArray();
        var removed = currentSet
            .Except(desiredFinalSet)
            .OrderBy(s => s, StringComparer.Ordinal)
            .ToArray();

        await AuthAuditWriter
            .TryAppendAsync(
                _auditLog,
                _logger,
                AuthAuditEventTypes.RolePermissionsChanged,
                request.ActorUserId,
                request.SourceIp,
                request.UserAgent,
                new
                {
                    targetRole = request.TargetRole.ToString(),
                    added,
                    removed,
                },
                request.CorrelationId,
                ct
            )
            .ConfigureAwait(false);

        return Result.Success();
    }
}
