using Npgsql;
using Testcontainers.PostgreSql;

namespace ShopFlow.Channel.IntegrationTests.Harness;

/// <summary>
/// Sprint-4.5 plan U4 — shared Testcontainers Postgres for the Channel
/// integration suite. One container per test collection; per-test multi-
/// tenant provisioning via <see cref="TenantWebhookHarness"/> on top.
/// Mirrors the <c>OutboundTenantFixture</c> / <c>InboundTenantFixture</c>
/// shape from prior sprints.
/// </summary>
/// <remarks>
/// The fixture only stands up Postgres + exposes the admin connection
/// string. Control-plane DB + tenant DB provisioning lives in
/// <see cref="TenantWebhookHarness"/> so each test class can choose its
/// tenant count (default 2-5) without disturbing the others.
/// </remarks>
public sealed class ChannelWebhookFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder()
        .WithImage("postgres:16")
        .WithDatabase("postgres")
        .WithUsername("postgres")
        .WithPassword("postgres")
        .Build();

    public string AdminConnectionString { get; private set; } = string.Empty;

    public async Task InitializeAsync()
    {
        await _container.StartAsync();
        AdminConnectionString = _container.GetConnectionString();
    }

    public async Task DisposeAsync()
    {
        await _container.DisposeAsync();
    }

    internal async Task<string> CreateDatabaseAsync(string dbName, CancellationToken ct = default)
    {
        await using var conn = new NpgsqlConnection(AdminConnectionString);
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"CREATE DATABASE \"{dbName}\"";
        await cmd.ExecuteNonQueryAsync(ct);
        return new NpgsqlConnectionStringBuilder(AdminConnectionString)
        {
            Database = dbName,
            MaxPoolSize = 25,
        }.ConnectionString;
    }
}

[CollectionDefinition(Name)]
public sealed class ChannelWebhookCollection : ICollectionFixture<ChannelWebhookFixture>
{
    public const string Name = "ChannelWebhook";
}
