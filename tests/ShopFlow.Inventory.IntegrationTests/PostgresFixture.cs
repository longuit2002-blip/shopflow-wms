using Microsoft.EntityFrameworkCore;
using NSubstitute;
using ShopFlow.Inventory.Infrastructure;
using ShopFlow.SharedKernel.Application;
using Testcontainers.PostgreSql;

namespace ShopFlow.Inventory.IntegrationTests;

/// <summary>
/// Shared <c>postgres:16-alpine</c> container per test collection. The
/// fixture starts the container once, applies the Inventory migration,
/// and exposes a connection-string + DbContext factory.
/// </summary>
public sealed class PostgresFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .WithDatabase("shopflow_inventory_tests")
        .WithUsername("postgres")
        .WithPassword("postgres")
        .Build();

    public string ConnectionString => _container.GetConnectionString();

    public async Task InitializeAsync()
    {
        await _container.StartAsync();

        // Apply the schema. We use a transient context with a stub
        // IRequestContext that returns a sentinel tenant id; migrations
        // do not require a live tenant.
        await using var ctx = CreateDbContext(Guid.Empty);
        await ctx.Database.MigrateAsync();
    }

    public Task DisposeAsync() => _container.DisposeAsync().AsTask();

    /// <summary>
    /// Build a fresh DbContext for the supplied tenant. The kernel's
    /// TenancyInterceptor stamps tenant_id on writes; the global query
    /// filter on the DbContext scopes reads.
    /// </summary>
    public InventoryDbContext CreateDbContext(Guid tenantId, string? correlationId = null)
    {
        var requestContext = Substitute.For<IRequestContext>();
        requestContext.TenantId.Returns(tenantId);
        requestContext.CorrelationId.Returns(correlationId ?? Guid.NewGuid().ToString("N"));

        var options = new DbContextOptionsBuilder<InventoryDbContext>()
            .UseNpgsql(ConnectionString)
            .Options;

        return new InventoryDbContext(options, requestContext);
    }
}

[CollectionDefinition("Integration")]
public sealed class IntegrationCollection : ICollectionFixture<PostgresFixture> { }
