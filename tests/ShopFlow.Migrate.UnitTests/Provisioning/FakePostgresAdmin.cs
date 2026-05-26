using ShopFlow.Migrate.Provisioning;

namespace ShopFlow.Migrate.UnitTests.Provisioning;

/// <summary>
/// In-memory <see cref="IPostgresAdmin"/> used by the
/// <see cref="TenantProvisionerTests"/>. Records every operation so tests
/// can assert ordering (CREATE before GRANT, REVOKE on archive). Replace
/// the implementation of <see cref="CreateDatabaseAsync"/> via
/// <see cref="CreateDatabaseHook"/> to simulate transient DDL failures.
/// </summary>
internal sealed class FakePostgresAdmin : IPostgresAdmin
{
    public List<string> Calls { get; } = new();

    public HashSet<string> Databases { get; } = new(StringComparer.Ordinal);

    public HashSet<string> Roles { get; } = new(StringComparer.Ordinal);

    public Func<string, Task>? CreateDatabaseHook { get; set; }

    public Task<bool> DatabaseExistsAsync(string dbName, CancellationToken ct)
    {
        Calls.Add($"DatabaseExists({dbName})");
        return Task.FromResult(Databases.Contains(dbName));
    }

    public async Task CreateDatabaseAsync(string dbName, CancellationToken ct)
    {
        Calls.Add($"CreateDatabase({dbName})");
        if (CreateDatabaseHook is not null)
        {
            await CreateDatabaseHook(dbName).ConfigureAwait(false);
        }
        Databases.Add(dbName);
    }

    public Task<bool> RoleExistsAsync(string roleName, CancellationToken ct)
    {
        Calls.Add($"RoleExists({roleName})");
        return Task.FromResult(Roles.Contains(roleName));
    }

    public Task EnsureLoginRoleAsync(string roleName, string password, CancellationToken ct)
    {
        Calls.Add($"EnsureLoginRole({roleName})");
        Roles.Add(roleName);
        return Task.CompletedTask;
    }

    public Task GrantTenantPrivilegesAsync(string dbName, string roleName, CancellationToken ct)
    {
        Calls.Add($"Grant({dbName},{roleName})");
        return Task.CompletedTask;
    }

    public Task RevokeTenantConnectAsync(string dbName, string roleName, CancellationToken ct)
    {
        Calls.Add($"Revoke({dbName},{roleName})");
        return Task.CompletedTask;
    }

    public Task RestoreTenantConnectAsync(string dbName, string roleName, CancellationToken ct)
    {
        Calls.Add($"Restore({dbName},{roleName})");
        return Task.CompletedTask;
    }
}
