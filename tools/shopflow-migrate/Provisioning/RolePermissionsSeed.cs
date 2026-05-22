using Microsoft.Extensions.Logging;
using Npgsql;
using ShopFlow.SharedKernel.Authorization;

namespace ShopFlow.Migrate.Provisioning;

/// <summary>
/// Sprint-9 U12 — seeds <c>role_permissions</c> with the Owner row
/// carrying every <see cref="PermissionKeys.All"/> entry. Sprint-11 U1
/// extends the seed to also insert the canonical Picker 4-key baseline
/// (<see cref="PickerBaseline"/>). Dispatcher still starts empty
/// (the Owner admin editor populates it via the Sprint-9.5 U7 surface).
/// Idempotent — re-running against a populated table inserts only
/// missing rows via <c>ON CONFLICT DO NOTHING</c> on the composite PK
/// <c>(role, permission_key)</c>.
/// </summary>
/// <remarks>
/// <para><b>Sprint-11 KTD1 — additive-only contract.</b> Every INSERT
/// uses <c>ON CONFLICT (role, permission_key) DO NOTHING</c>:
/// Owner additions made via the admin editor beyond baseline are
/// preserved across re-seeds, and deletions of any baseline row are
/// reverted on the next provision (re-insertion of the missing key).
/// The seed never DELETEs, never UPDATEs — it only ensures the
/// baseline set is present.</para>
/// </remarks>
public sealed class RolePermissionsSeed
{
    /// <summary>
    /// Sprint-11 U1 — the canonical 4-key Picker baseline pre-seeded at
    /// every tenant provision. Frontend mirror lives at
    /// <c>web/src/lib/auth/pickerBaseline.ts</c>; both lists must stay
    /// in lock-step (the perm strings are the shared contract).
    /// </summary>
    public static readonly IReadOnlyList<string> PickerBaseline = new[]
    {
        PermissionKeys.OutboundOrdersRead,
        PermissionKeys.OutboundOrdersPickConfirm,
        PermissionKeys.InventoryRead,
        PermissionKeys.HubConnect,
    };

    private readonly ILogger<RolePermissionsSeed> _logger;

    public RolePermissionsSeed(ILogger<RolePermissionsSeed> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Seed the Owner row with every PermissionKeys.All entry and the
    /// Picker row with the canonical baseline. Safe to run multiple
    /// times (additive-only via ON CONFLICT DO NOTHING).
    /// </summary>
    public async Task SeedAsync(string tenantConnectionString, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantConnectionString);

        await using var conn = new NpgsqlConnection(tenantConnectionString);
        await conn.OpenAsync(ct).ConfigureAwait(false);

        // Sprint-9 — Owner gets every key. Sprint-11 — Picker gets the
        // 4-key baseline. PK(role, permission_key) makes the seed
        // idempotent: ON CONFLICT DO NOTHING.
        await using var tx = await conn.BeginTransactionAsync(ct).ConfigureAwait(false);
        try
        {
            foreach (var key in PermissionKeys.All)
            {
                await InsertAsync(conn, tx, "Owner", key, ct).ConfigureAwait(false);
            }

            foreach (var key in PickerBaseline)
            {
                await InsertAsync(conn, tx, "Picker", key, ct).ConfigureAwait(false);
            }

            await tx.CommitAsync(ct).ConfigureAwait(false);
        }
        catch
        {
            await tx.RollbackAsync(ct).ConfigureAwait(false);
            throw;
        }

        _logger.LogInformation(
            "RolePermissionsSeed: ensured Owner row has {OwnerCount} permission keys and Picker row has {PickerCount} baseline keys.",
            PermissionKeys.All.Count,
            PickerBaseline.Count);
    }

    private static async Task InsertAsync(
        NpgsqlConnection conn,
        NpgsqlTransaction tx,
        string role,
        string key,
        CancellationToken ct)
    {
        await using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText =
            "INSERT INTO role_permissions (role, permission_key, created_at) "
            + "VALUES (@role, @key, NOW()) "
            + "ON CONFLICT (role, permission_key) DO NOTHING;";
        cmd.Parameters.AddWithValue("role", role);
        cmd.Parameters.AddWithValue("key", key);
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }
}
