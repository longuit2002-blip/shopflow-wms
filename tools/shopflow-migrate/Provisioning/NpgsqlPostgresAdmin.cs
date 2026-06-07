using Microsoft.Extensions.Logging;
using Npgsql;

namespace ShopFlow.Migrate.Provisioning;

/// <summary>
/// Default <see cref="IPostgresAdmin"/> over <c>Npgsql</c>. All operations
/// open a fresh connection — DDL under PgBouncer transaction-pooling is
/// forbidden, so the admin connection string points directly at Postgres
/// (not the pooler).
/// </summary>
/// <remarks>
/// <para>Identifier quoting uses double-quotes for DB / role names; we
/// validate inputs against a strict allowlist (<c>[a-z0-9_]</c>) before
/// interpolation so the operations cannot be turned into SQL injection.
/// Catalog-controlled values (slug-derived db_name, the configured app
/// role name) are the only inputs; user-typed strings never reach this
/// layer.</para>
/// </remarks>
public sealed class NpgsqlPostgresAdmin : IPostgresAdmin
{
    private readonly string _adminConnectionString;
    private readonly ILogger<NpgsqlPostgresAdmin> _logger;

    public NpgsqlPostgresAdmin(string adminConnectionString, ILogger<NpgsqlPostgresAdmin> logger)
    {
        if (string.IsNullOrWhiteSpace(adminConnectionString))
        {
            throw new ArgumentException(
                "admin connection string is required.",
                nameof(adminConnectionString)
            );
        }
        _adminConnectionString = adminConnectionString;
        _logger = logger;
    }

    public async Task<bool> DatabaseExistsAsync(string dbName, CancellationToken ct)
    {
        ValidateIdentifier(dbName, nameof(dbName));
        await using var conn = await OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = new NpgsqlCommand(
            "SELECT 1 FROM pg_database WHERE datname = @n",
            conn
        );
        cmd.Parameters.AddWithValue("n", dbName);
        var result = await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false);
        return result is not null;
    }

    public async Task CreateDatabaseAsync(string dbName, CancellationToken ct)
    {
        ValidateIdentifier(dbName, nameof(dbName));
        await using var conn = await OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = new NpgsqlCommand(
            $"CREATE DATABASE \"{dbName}\" ENCODING 'UTF8'",
            conn
        );
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        _logger.LogInformation("Created database {DbName}.", dbName);
    }

    public async Task<bool> RoleExistsAsync(string roleName, CancellationToken ct)
    {
        ValidateIdentifier(roleName, nameof(roleName));
        await using var conn = await OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = new NpgsqlCommand("SELECT 1 FROM pg_roles WHERE rolname = @n", conn);
        cmd.Parameters.AddWithValue("n", roleName);
        var result = await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false);
        return result is not null;
    }

    public async Task EnsureLoginRoleAsync(string roleName, string password, CancellationToken ct)
    {
        ValidateIdentifier(roleName, nameof(roleName));
        if (string.IsNullOrEmpty(password))
        {
            throw new ArgumentException("role password is required.", nameof(password));
        }

        if (await RoleExistsAsync(roleName, ct).ConfigureAwait(false))
        {
            return;
        }

        await using var conn = await OpenAsync(ct).ConfigureAwait(false);
        // Password literal cannot be parameterised in CREATE ROLE; we
        // escape single-quotes by doubling per Postgres lexer rules.
        var escaped = password.Replace("'", "''", StringComparison.Ordinal);
        await using var cmd = new NpgsqlCommand(
            $"CREATE ROLE \"{roleName}\" LOGIN PASSWORD '{escaped}'",
            conn
        );
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        _logger.LogInformation("Created login role {RoleName}.", roleName);
    }

    public async Task GrantTenantPrivilegesAsync(
        string dbName,
        string roleName,
        CancellationToken ct
    )
    {
        ValidateIdentifier(dbName, nameof(dbName));
        ValidateIdentifier(roleName, nameof(roleName));

        await using (var conn = await OpenAsync(ct).ConfigureAwait(false))
        {
            await Exec(conn, $"GRANT CONNECT ON DATABASE \"{dbName}\" TO \"{roleName}\"", ct)
                .ConfigureAwait(false);
        }

        // Schema/object grants must run inside the target DB, not the admin DB.
        var builder = new NpgsqlConnectionStringBuilder(_adminConnectionString)
        {
            Database = dbName,
        };
        await using var tenantConn = new NpgsqlConnection(builder.ConnectionString);
        await tenantConn.OpenAsync(ct).ConfigureAwait(false);
        await Exec(tenantConn, $"GRANT USAGE ON SCHEMA public TO \"{roleName}\"", ct)
            .ConfigureAwait(false);
        await Exec(
                tenantConn,
                $"GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA public TO \"{roleName}\"",
                ct
            )
            .ConfigureAwait(false);
        await Exec(
                tenantConn,
                $"GRANT USAGE, SELECT, UPDATE ON ALL SEQUENCES IN SCHEMA public TO \"{roleName}\"",
                ct
            )
            .ConfigureAwait(false);
        await Exec(
                tenantConn,
                $"ALTER DEFAULT PRIVILEGES IN SCHEMA public GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO \"{roleName}\"",
                ct
            )
            .ConfigureAwait(false);
        await Exec(
                tenantConn,
                $"ALTER DEFAULT PRIVILEGES IN SCHEMA public GRANT USAGE, SELECT, UPDATE ON SEQUENCES TO \"{roleName}\"",
                ct
            )
            .ConfigureAwait(false);

        _logger.LogInformation(
            "Granted DML privileges on {DbName} to {RoleName}.",
            dbName,
            roleName
        );
    }

    public async Task RevokeTenantConnectAsync(string dbName, string roleName, CancellationToken ct)
    {
        ValidateIdentifier(dbName, nameof(dbName));
        ValidateIdentifier(roleName, nameof(roleName));

        await using var conn = await OpenAsync(ct).ConfigureAwait(false);
        await Exec(conn, $"REVOKE CONNECT ON DATABASE \"{dbName}\" FROM \"{roleName}\"", ct)
            .ConfigureAwait(false);
        await Exec(
                conn,
                $"SELECT pg_terminate_backend(pid) FROM pg_stat_activity WHERE datname = '{dbName.Replace("'", "''", StringComparison.Ordinal)}' AND pid <> pg_backend_pid()",
                ct
            )
            .ConfigureAwait(false);
        _logger.LogInformation("Revoked CONNECT on {DbName} from {RoleName}.", dbName, roleName);
    }

    public async Task RestoreTenantConnectAsync(
        string dbName,
        string roleName,
        CancellationToken ct
    )
    {
        ValidateIdentifier(dbName, nameof(dbName));
        ValidateIdentifier(roleName, nameof(roleName));

        await using var conn = await OpenAsync(ct).ConfigureAwait(false);
        await Exec(conn, $"GRANT CONNECT ON DATABASE \"{dbName}\" TO \"{roleName}\"", ct)
            .ConfigureAwait(false);
        _logger.LogInformation("Restored CONNECT on {DbName} to {RoleName}.", dbName, roleName);
    }

    private async Task<NpgsqlConnection> OpenAsync(CancellationToken ct)
    {
        var conn = new NpgsqlConnection(_adminConnectionString);
        await conn.OpenAsync(ct).ConfigureAwait(false);
        return conn;
    }

    private static async Task Exec(NpgsqlConnection conn, string sql, CancellationToken ct)
    {
        await using var cmd = new NpgsqlCommand(sql, conn);
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Identifiers reach this layer only from catalog rows (slug-derived
    /// db_name) or from configured role names. Enforce the same allowlist
    /// Postgres' own naming conventions imply — lowercase, digits,
    /// underscore — so a stray config value cannot escape the quotes.
    /// </summary>
    internal static void ValidateIdentifier(string value, string paramName)
    {
        if (string.IsNullOrEmpty(value))
        {
            throw new ArgumentException("identifier is required.", paramName);
        }

        foreach (var c in value)
        {
            var ok = c is (>= 'a' and <= 'z') or (>= '0' and <= '9') or '_';
            if (!ok)
            {
                throw new ArgumentException(
                    $"identifier '{value}' contains illegal character '{c}'. Expected lowercase letters, digits, underscore.",
                    paramName
                );
            }
        }
    }
}
