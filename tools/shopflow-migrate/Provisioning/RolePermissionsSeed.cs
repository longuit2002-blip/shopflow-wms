using Microsoft.Extensions.Logging;
using Npgsql;
using ShopFlow.SharedKernel.Authorization;

namespace ShopFlow.Migrate.Provisioning;

/// <summary>
/// Sprint-9 U12 — seeds <c>role_permissions</c> with the Owner row
/// carrying every <see cref="PermissionKeys.All"/> entry. Picker +
/// Dispatcher start empty (the Owner admin editor populates them via
/// the U9 surface). Idempotent — re-running against a populated table
/// inserts only missing rows.
/// </summary>
public sealed class RolePermissionsSeed
{
    private readonly ILogger<RolePermissionsSeed> _logger;

    public RolePermissionsSeed(ILogger<RolePermissionsSeed> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Seed the Owner row with every PermissionKeys.All entry; ensure
    /// Picker + Dispatcher rows exist (empty if absent). Safe to run
    /// multiple times.
    /// </summary>
    public async Task SeedAsync(string tenantConnectionString, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantConnectionString);

        await using var conn = new NpgsqlConnection(tenantConnectionString);
        await conn.OpenAsync(ct).ConfigureAwait(false);

        // Sprint-9 — Owner gets every key. UNIQUE(role, permission_key)
        // makes the seed idempotent: ON CONFLICT DO NOTHING.
        await using var tx = await conn.BeginTransactionAsync(ct).ConfigureAwait(false);
        try
        {
            foreach (var key in PermissionKeys.All)
            {
                await using var cmd = conn.CreateCommand();
                cmd.Transaction = tx;
                cmd.CommandText =
                    "INSERT INTO role_permissions (role, permission_key, created_at) "
                    + "VALUES ('Owner', @key, NOW()) "
                    + "ON CONFLICT (role, permission_key) DO NOTHING;";
                cmd.Parameters.AddWithValue("key", key);
                await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            }

            await tx.CommitAsync(ct).ConfigureAwait(false);
        }
        catch
        {
            await tx.RollbackAsync(ct).ConfigureAwait(false);
            throw;
        }

        _logger.LogInformation(
            "RolePermissionsSeed: ensured Owner row has {Count} permission keys.",
            PermissionKeys.All.Count);
    }
}
