namespace ShopFlow.Migrate.Provisioning;

/// <summary>
/// Superuser-level DDL operations the CLI needs to drive tenant lifecycle.
/// Lives behind an interface so the provisioning workflow is unit-testable
/// without a live Postgres (see <c>FakePostgresAdmin</c> in the test
/// project). Implementation calls run against the
/// <see cref="MigrateOptions.Postgres"/> admin connection (bypasses
/// PgBouncer per AGENTS.md §3.20).
/// </summary>
public interface IPostgresAdmin
{
    Task<bool> DatabaseExistsAsync(string dbName, CancellationToken ct);

    Task CreateDatabaseAsync(string dbName, CancellationToken ct);

    Task<bool> RoleExistsAsync(string roleName, CancellationToken ct);

    /// <summary>Create a LOGIN role with the supplied password. Idempotent — no-ops when the role already exists.</summary>
    Task EnsureLoginRoleAsync(string roleName, string password, CancellationToken ct);

    /// <summary>
    /// Grant the app role connect + DML privileges on the named tenant DB.
    /// Must be re-run after migrations because <c>ALTER DEFAULT PRIVILEGES</c>
    /// only applies to objects created after the GRANT.
    /// </summary>
    Task GrantTenantPrivilegesAsync(string dbName, string roleName, CancellationToken ct);

    /// <summary>Revoke CONNECT on the named DB and terminate live sessions. Used by <c>archive</c>.</summary>
    Task RevokeTenantConnectAsync(string dbName, string roleName, CancellationToken ct);

    /// <summary>Re-grant CONNECT (reverse of <see cref="RevokeTenantConnectAsync"/>). Used by <c>restore</c>.</summary>
    Task RestoreTenantConnectAsync(string dbName, string roleName, CancellationToken ct);
}
