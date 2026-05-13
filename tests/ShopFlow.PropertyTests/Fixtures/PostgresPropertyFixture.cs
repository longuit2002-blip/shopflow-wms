using Microsoft.EntityFrameworkCore;
using Npgsql;
using ShopFlow.Inventory.Application.Ports;
using ShopFlow.Inventory.Domain;
using ShopFlow.Inventory.Infrastructure;
using ShopFlow.Inventory.Infrastructure.Repositories;
using ShopFlow.PropertyTests.Stubs;
using ShopFlow.SharedKernel.Application;
using ShopFlow.SharedKernel.Application.Ports;
using ShopFlow.SharedKernel.Domain;
using Testcontainers.PostgreSql;

namespace ShopFlow.PropertyTests.Fixtures;

/// <summary>
/// Per-collection Testcontainers Postgres + a single provisioned tenant
/// database. On construction the fixture installs a real
/// <c>ReservationRepository</c> bound to that tenant DB into
/// <see cref="ReservationRepositoryHandle.Current"/>, so the
/// <see cref="NotImplementedReservationRepository"/> adapter forwards
/// straight to the live impl. Each property re-seeds its own SKU rows
/// (via <see cref="ResetForPropertyAsync"/>) before the inner FsCheck
/// iterations run.
/// </summary>
public sealed class PostgresPropertyFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder()
        .WithImage("postgres:16")
        .WithDatabase("postgres")
        .WithUsername("postgres")
        .WithPassword("postgres")
        .Build();

    public string ConnectionString { get; private set; } = string.Empty;

    public DbContextOptions<InventoryDbContext> Options { get; private set; } = default!;

    public TenantInfo Tenant { get; private set; } = default!;

    public async Task InitializeAsync()
    {
        await _container.StartAsync();
        var admin = _container.GetConnectionString();
        var dbName = "shopflow_prop_" + Guid.NewGuid().ToString("N")[..8];

        await using (var adminConn = new NpgsqlConnection(admin))
        {
            await adminConn.OpenAsync();
            await using var cmd = adminConn.CreateCommand();
            cmd.CommandText = $"CREATE DATABASE \"{dbName}\"";
            await cmd.ExecuteNonQueryAsync();
        }

        ConnectionString = new NpgsqlConnectionStringBuilder(admin) { Database = dbName }.ConnectionString;

        Options = new DbContextOptionsBuilder<InventoryDbContext>()
            .UseNpgsql(
                ConnectionString,
                npg => npg.MigrationsAssembly("ShopFlow.Inventory.Infrastructure")
            )
            .Options;

        await using (var ctx = new InventoryDbContext(Options))
        {
            await ctx.Database.MigrateAsync();
        }

        Tenant = new TenantInfo(
            Id: Guid.NewGuid(),
            Slug: "prop",
            DbName: dbName,
            DbConnectionString: ConnectionString,
            Region: "ap-southeast-1",
            Tier: "free",
            Status: TenantStatus.Ready
        );

        // Install a long-lived "live" repository handle. FsCheck constructs
        // NotImplementedReservationRepository per property iteration; each
        // forwards through this handle to a fresh ReservationRepository so
        // every call binds to a fresh DbContext + RequestContext.
        ReservationRepositoryHandle.Current = new HandleAdapter(this);
    }

    public Task DisposeAsync()
    {
        ReservationRepositoryHandle.Current = null;
        return _container.DisposeAsync().AsTask();
    }

    /// <summary>
    /// Re-seed <c>stock_items</c> with a clean row for the given SKU at
    /// the given <paramref name="available"/>. Truncates the ledger so
    /// the property starts from an empty state.
    /// </summary>
    public async Task ResetForPropertyAsync(string sku, int available)
    {
        await using var conn = new NpgsqlConnection(ConnectionString);
        await conn.OpenAsync();
        await using (var truncate = conn.CreateCommand())
        {
            truncate.CommandText = """
                DELETE FROM reservations_ledger;
                DELETE FROM inventory_outbox_messages;
                DELETE FROM stock_items;
                """;
            await truncate.ExecuteNonQueryAsync();
        }
        await using var seed = conn.CreateCommand();
        seed.CommandText = """
            INSERT INTO stock_items (sku, available, reserved, created_at)
            VALUES (@sku, @avail, 0, @now)
            """;
        seed.Parameters.AddWithValue("sku", sku);
        seed.Parameters.AddWithValue("avail", available);
        seed.Parameters.AddWithValue("now", DateTime.UtcNow);
        await seed.ExecuteNonQueryAsync();
    }

    /// <summary>
    /// Forward every IReservationRepository call to a fresh repository
    /// instance backed by a fresh DbContext + RequestContext. Property
    /// tests issue concurrent calls; sharing one DbContext across them
    /// would surface as EF Core thread-safety failures unrelated to the
    /// spec.
    /// </summary>
    private sealed class HandleAdapter : IReservationRepository
    {
        private readonly PostgresPropertyFixture _fx;

        public HandleAdapter(PostgresPropertyFixture fx)
        {
            _fx = fx;
        }

        public Task<Result<Reservation>> TryReserveAsync(
            Sku sku,
            string orderId,
            Quantity quantity,
            TimeSpan ttl,
            CancellationToken ct
        ) => RunAsync(repo => repo.TryReserveAsync(sku, orderId, quantity, ttl, ct));

        public Task<Reservation?> FindByOrderIdAsync(string orderId, CancellationToken ct) =>
            RunAsync(repo => repo.FindByOrderIdAsync(orderId, ct));

        public Task<Result> ConfirmAsync(string orderId, CancellationToken ct) =>
            RunAsync(repo => repo.ConfirmAsync(orderId, ct));

        public Task<Result> ReleaseAsync(string orderId, CancellationToken ct) =>
            RunAsync(repo => repo.ReleaseAsync(orderId, ct));

        public Task<int> ReleaseExpiredAsync(DateTime now, int batchSize, CancellationToken ct) =>
            RunAsync(repo => repo.ReleaseExpiredAsync(now, batchSize, ct));

        private async Task<T> RunAsync<T>(Func<ReservationRepository, Task<T>> body)
        {
            var db = new InventoryDbContext(_fx.Options);
            try
            {
                var rc = new RequestContext();
                rc.Bind(_fx.Tenant, Guid.NewGuid().ToString("N"), userId: null);
                var repo = new ReservationRepository(db, TimeProvider.System, rc);
                return await body(repo).ConfigureAwait(false);
            }
            finally
            {
                await db.DisposeAsync().ConfigureAwait(false);
            }
        }
    }
}

[CollectionDefinition(Name)]
public sealed class PostgresPropertyCollection : ICollectionFixture<PostgresPropertyFixture>
{
    public const string Name = "PostgresProperty";
}
