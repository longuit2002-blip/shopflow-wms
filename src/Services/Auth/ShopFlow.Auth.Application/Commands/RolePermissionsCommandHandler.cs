using MediatR;
using ShopFlow.Auth.Application.Ports;
using ShopFlow.Auth.Domain;
using ShopFlow.SharedKernel.Authorization;
using ShopFlow.SharedKernel.Domain;

namespace ShopFlow.Auth.Application.Commands;

/// <summary>
/// Sprint-9 U8 — Owner-only RBAC editor. KTD13 — server-side guard
/// rejects any edit that would leave the Owner row missing one of
/// <see cref="PermissionKeys.OwnerCritical"/> with 422
/// <c>auth.role_permissions_owner_critical_locked</c>.
/// </summary>
public sealed class RolePermissionsCommandHandler : IRequestHandler<RolePermissionsCommand, Result>
{
    private const string OwnerCriticalLocked = "auth.role_permissions_owner_critical_locked";
    private const string UnknownKey = "auth.role_permissions_unknown_key";
    private const string OperationInvalid = "auth.role_permissions_operation_invalid";

    private readonly IRolePermissionRepository _repo;

    public RolePermissionsCommandHandler(IRolePermissionRepository repo)
    {
        _repo = repo;
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
                    return Result.Failure($"Unknown permission key: {request.PermissionKey}", UnknownKey);
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
                        OwnerCriticalLocked);
                }
            }
        }

        var update = await _repo.UpdateForRoleAsync(request.TargetRole, desired, ct).ConfigureAwait(false);
        return update.IsSuccess ? Result.Success() : Result.Failure(update.Error!, update.ErrorCode);
    }
}
