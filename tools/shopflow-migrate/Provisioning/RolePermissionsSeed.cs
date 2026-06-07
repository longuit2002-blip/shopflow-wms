using Microsoft.Extensions.Logging;
using Npgsql;
using ShopFlow.SharedKernel.Authorization;

namespace ShopFlow.Migrate.Provisioning;

/// <summary>
/// Sprint-9 U12 — seeds <c>role_permissions</c> with the Owner row
/// carrying every <see cref="PermissionKeys.All"/> entry. Sprint-11 U1
/// extends the seed to also insert the canonical Picker 4-key baseline
/// (<see cref="PickerBaseline"/>). Sprint-12 U1 extends further with
/// the canonical Dispatcher 3-key baseline
/// (<see cref="DispatcherBaseline"/>). Sprint-13 U1 adds the canonical
/// Packer 3-key baseline (<see cref="PackerBaseline"/>). Idempotent —
/// re-running against a populated table inserts only missing rows via
/// <c>ON CONFLICT DO NOTHING</c> on the composite PK
/// <c>(role, permission_key)</c>.
/// </summary>
/// <remarks>
/// <para><b>Sprint-11 KTD1 — additive-only contract.</b> Every INSERT
/// uses <c>ON CONFLICT (role, permission_key) DO NOTHING</c>:
/// Owner additions made via the admin editor beyond baseline are
/// preserved across re-seeds, and deletions of any baseline row are
/// reverted on the next provision (re-insertion of the missing key).
/// The seed never DELETEs, never UPDATEs — it only ensures the
/// baseline set is present. Same contract holds for Picker
/// (Sprint-11), Dispatcher (Sprint-12) AND Packer (Sprint-13).</para>
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

    /// <summary>
    /// Sprint-12 U1 — the canonical 3-key Dispatcher baseline
    /// pre-seeded at every tenant provision. Dispatcher owns the
    /// ship-confirm transition (Owner pack-confirms; Picker
    /// pick-confirms; Dispatcher ship-confirms). Frontend mirror lives
    /// at <c>web/src/lib/auth/dispatcherBaseline.ts</c>; both lists
    /// must stay in lock-step.
    /// </summary>
    /// <remarks>
    /// <para><b>Important — DOES NOT contain
    /// <c>outbound.orders.pack-confirm</c>.</b> Pack stays Owner-only
    /// at Sprint-12 (no Packer fourth role). The hand-off chain is
    /// Picker → Owner → Dispatcher on one saga.</para>
    /// </remarks>
    public static readonly IReadOnlyList<string> DispatcherBaseline = new[]
    {
        PermissionKeys.OutboundOrdersRead,
        PermissionKeys.OutboundOrdersShipConfirm,
        PermissionKeys.HubConnect,
    };

    /// <summary>
    /// Sprint-13 U1 (K5) — the canonical 3-key Packer baseline
    /// pre-seeded at every tenant provision. Packer owns the
    /// pack-confirm transition (Sprint-13 moves Pack off Owner). The
    /// 4-role hand-off chain is Picker → Packer → Dispatcher on one
    /// saga. Mirrors <see cref="DispatcherBaseline"/>'s shape (3 keys,
    /// no <c>inventory.read</c>) — by pack time items are already
    /// pulled, so Packer doesn't need inventory visibility.
    /// </summary>
    /// <remarks>
    /// <para><b>Reuses <c>outbound.orders.pack-confirm</c>.</b> The same
    /// key gates both <c>confirm-pack</c> AND <c>mark-pack-failed</c>
    /// (Sprint-13 K3 — no 25th permission key). No frontend mirror at
    /// Sprint-13 (backend-only); a <c>web/src/lib/auth/packerBaseline.ts</c>
    /// mirror lands when the Packer UI surface ships.</para>
    ///
    /// <para><b>Owner KEEPS pack-confirm</b> (Sprint-13 K7 — ADDITIVE-ONLY
    /// KTD1). Packer is added beside Owner, not in place of it.</para>
    /// </remarks>
    public static readonly IReadOnlyList<string> PackerBaseline = new[]
    {
        PermissionKeys.OutboundOrdersRead,
        PermissionKeys.OutboundOrdersPackConfirm,
        PermissionKeys.HubConnect,
    };

    private readonly ILogger<RolePermissionsSeed> _logger;

    public RolePermissionsSeed(ILogger<RolePermissionsSeed> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Seed the Owner row with every PermissionKeys.All entry, the
    /// Picker row with its canonical baseline, the Dispatcher row with
    /// its canonical baseline, and the Packer row with its canonical
    /// baseline. Safe to run multiple times (additive-only via
    /// ON CONFLICT DO NOTHING).
    /// </summary>
    public async Task SeedAsync(string tenantConnectionString, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantConnectionString);

        await using var conn = new NpgsqlConnection(tenantConnectionString);
        await conn.OpenAsync(ct).ConfigureAwait(false);

        // Sprint-9 — Owner gets every key. Sprint-11 — Picker gets the
        // 4-key baseline. Sprint-12 — Dispatcher gets the 3-key
        // baseline. Sprint-13 — Packer gets the 3-key baseline.
        // PK(role, permission_key) makes the seed idempotent:
        // ON CONFLICT DO NOTHING.
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

            foreach (var key in DispatcherBaseline)
            {
                await InsertAsync(conn, tx, "Dispatcher", key, ct).ConfigureAwait(false);
            }

            foreach (var key in PackerBaseline)
            {
                await InsertAsync(conn, tx, "Packer", key, ct).ConfigureAwait(false);
            }

            await tx.CommitAsync(ct).ConfigureAwait(false);
        }
        catch
        {
            await tx.RollbackAsync(ct).ConfigureAwait(false);
            throw;
        }

        _logger.LogInformation(
            "RolePermissionsSeed: ensured Owner row has {OwnerCount} permission keys, Picker row has {PickerCount} baseline keys, Dispatcher row has {DispatcherCount} baseline keys, Packer row has {PackerCount} baseline keys.",
            PermissionKeys.All.Count,
            PickerBaseline.Count,
            DispatcherBaseline.Count,
            PackerBaseline.Count
        );
    }

    private static async Task InsertAsync(
        NpgsqlConnection conn,
        NpgsqlTransaction tx,
        string role,
        string key,
        CancellationToken ct
    )
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
