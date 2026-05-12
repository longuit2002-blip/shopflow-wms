using Npgsql;
using Testcontainers.PostgreSql;

namespace ShopFlow.SharedKernel.IntegrationTests;

/// <summary>
/// xUnit class fixture that spins one Postgres 16 container per test class
/// and exposes the admin connection string. Tests that need fresh
/// databases call <see cref="CreateDatabaseAsync"/>; tests are expected
/// to write to that database and leave it behind (the container goes
/// away at fixture dispose, so cleanup is implicit).
/// </summary>
/// <remarks>
/// Image tag pinned to <c>postgres:16</c> per AGENTS.md §8.56. The
/// fixture starts the container in <c>InitializeAsync</c> and disposes
/// in <c>DisposeAsync</c>; xUnit handles the lifecycle.
/// </remarks>
public sealed class PostgresFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder()
        .WithImage("postgres:16")
        .WithDatabase("postgres")
        .WithUsername("postgres")
        .WithPassword("postgres")
        .Build();

    public string AdminConnectionString { get; private set; } = string.Empty;

    public string BuildDbConnectionString(string dbName)
    {
        var b = new NpgsqlConnectionStringBuilder(AdminConnectionString) { Database = dbName };
        return b.ConnectionString;
    }

    public async Task<string> CreateDatabaseAsync(string dbName, CancellationToken ct = default)
    {
        await using var conn = new NpgsqlConnection(AdminConnectionString);
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"CREATE DATABASE \"{dbName}\"";
        await cmd.ExecuteNonQueryAsync(ct);
        return BuildDbConnectionString(dbName);
    }

    public async Task InitializeAsync()
    {
        await _container.StartAsync();
        AdminConnectionString = _container.GetConnectionString();
    }

    public async Task DisposeAsync()
    {
        await _container.DisposeAsync();
    }
}

/// <summary>
/// xUnit collection that pins the shared Postgres fixture to every test
/// class in this assembly. One container, many tests — startup is the
/// expensive part, so amortising it across tests keeps the integration
/// suite under the per-PR budget.
/// </summary>
[CollectionDefinition(Name)]
public sealed class PostgresCollection : ICollectionFixture<PostgresFixture>
{
    public const string Name = "Postgres";
}
