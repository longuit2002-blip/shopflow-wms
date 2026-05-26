using Microsoft.EntityFrameworkCore;
using ShopFlow.Auth.Application.Ports;
using ShopFlow.Auth.Domain;
using ShopFlow.Auth.Domain.Entities;
using ShopFlow.SharedKernel.Authorization;
using ShopFlow.SharedKernel.Domain;

namespace ShopFlow.Auth.Infrastructure.Repositories;

/// <summary>
/// Sprint-9 U3 EF Core impl of <see cref="IRolePermissionRepository"/>.
/// <see cref="UpdateForRoleAsync"/> diffs the desired list against the
/// current rows and inserts / deletes the difference inside one
/// transaction. KTD13 OwnerCritical guard lives in the handler at U8 —
/// the repository rejects unknown keys but does not enforce the
/// Owner-critical invariant.
/// </summary>
public sealed class RolePermissionRepository : IRolePermissionRepository
{
    private readonly AuthDbContext _db;

    public RolePermissionRepository(AuthDbContext db)
    {
        _db = db;
    }

    public async Task<IReadOnlyList<string>> GetForRoleAsync(UserRole role, CancellationToken ct)
    {
        return await _db
            .RolePermissions.AsNoTracking()
            .Where(rp => rp.Role == role)
            .Select(rp => rp.PermissionKey)
            .ToListAsync(ct)
            .ConfigureAwait(false);
    }

    public async Task<IReadOnlyDictionary<UserRole, IReadOnlyList<string>>> ListAllAsync(
        CancellationToken ct
    )
    {
        var rows = await _db.RolePermissions.AsNoTracking().ToListAsync(ct).ConfigureAwait(false);

        var byRole = rows.GroupBy(rp => rp.Role)
            .ToDictionary(
                g => g.Key,
                g => (IReadOnlyList<string>)g.Select(rp => rp.PermissionKey).ToList()
            );

        foreach (var role in Enum.GetValues<UserRole>())
        {
            if (!byRole.ContainsKey(role))
            {
                byRole[role] = Array.Empty<string>();
            }
        }
        return byRole;
    }

    public async Task<Result> UpdateForRoleAsync(
        UserRole role,
        IReadOnlyList<string> permissionKeys,
        CancellationToken ct
    )
    {
        ArgumentNullException.ThrowIfNull(permissionKeys);

        var allKnown = PermissionKeys.All.ToHashSet(StringComparer.Ordinal);
        foreach (var key in permissionKeys)
        {
            if (!allKnown.Contains(key))
            {
                return Result.Failure(
                    $"Unknown permission key: {key}",
                    "auth.role_permissions_unknown_key"
                );
            }
        }

        var desired = permissionKeys.ToHashSet(StringComparer.Ordinal);
        var existing = await _db
            .RolePermissions.Where(rp => rp.Role == role)
            .ToListAsync(ct)
            .ConfigureAwait(false);
        var existingKeys = existing
            .Select(rp => rp.PermissionKey)
            .ToHashSet(StringComparer.Ordinal);

        // Drop rows that are no longer desired.
        var toDelete = existing.Where(rp => !desired.Contains(rp.PermissionKey)).ToList();
        if (toDelete.Count > 0)
        {
            _db.RolePermissions.RemoveRange(toDelete);
        }

        // Insert rows that are newly desired.
        var now = DateTime.UtcNow;
        foreach (var key in desired.Where(k => !existingKeys.Contains(k)))
        {
            await _db
                .RolePermissions.AddAsync(RolePermission.Grant(role, key, now), ct)
                .ConfigureAwait(false);
        }

        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Result.Success();
    }
}
