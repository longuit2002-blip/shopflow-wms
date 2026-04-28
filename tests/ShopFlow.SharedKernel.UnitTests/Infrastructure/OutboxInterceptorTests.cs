using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using ShopFlow.SharedKernel.Infrastructure;
using Xunit;

namespace ShopFlow.SharedKernel.UnitTests.Infrastructure;

public class OutboxInterceptorTests : IAsyncLifetime
{
    private SqliteConnection _connection = null!;

    public Task InitializeAsync()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        _connection.Dispose();
        return Task.CompletedTask;
    }

    [Fact]
    public async Task SavingChanges_PersistsDomainEventToOutbox_AndClearsBuffer()
    {
        var tenantId = Guid.NewGuid();
        await using var ctx = NewContext();
        await ctx.Database.EnsureCreatedAsync();

        var widget = new Widget(tenantId, "alpha");
        widget.RaiseTestEvent();
        ctx.Widgets.Add(widget);

        await ctx.SaveChangesAsync();

        var outboxRows = await ctx.Outbox.ToListAsync();
        outboxRows.Should().HaveCount(1);
        outboxRows[0].TenantId.Should().Be(tenantId);
        outboxRows[0].EventType.Should().Contain("WidgetChangedEvent");
        outboxRows[0].Payload.Should().Contain("alpha");
        outboxRows[0].ProcessedAt.Should().BeNull();
        outboxRows[0].RetryCount.Should().Be(0);

        // Buffer drained on the in-memory entity.
        widget.DomainEvents.Should().BeEmpty();
    }

    [Fact]
    public async Task SavingChanges_WritesNothingToOutbox_WhenNoEventsRaised()
    {
        var tenantId = Guid.NewGuid();
        await using var ctx = NewContext();
        await ctx.Database.EnsureCreatedAsync();

        ctx.Widgets.Add(new Widget(tenantId, "no-events"));

        await ctx.SaveChangesAsync();

        var outboxRows = await ctx.Outbox.CountAsync();
        outboxRows.Should().Be(0);
    }

    [Fact]
    public async Task SavingChanges_WritesMultipleRows_WhenAggregateRaisesMultipleEvents()
    {
        var tenantId = Guid.NewGuid();
        await using var ctx = NewContext();
        await ctx.Database.EnsureCreatedAsync();

        var widget = new Widget(tenantId, "multi");
        widget.RaiseTestEvent();
        widget.RaiseTestEvent();
        widget.RaiseTestEvent();
        ctx.Widgets.Add(widget);

        await ctx.SaveChangesAsync();

        var outboxRows = await ctx.Outbox.CountAsync();
        outboxRows.Should().Be(3);
    }

    [Fact]
    public async Task SavingChanges_TransactionRolledBack_LeavesNoOutboxRow()
    {
        var tenantId = Guid.NewGuid();
        await using var ctx = NewContext();
        await ctx.Database.EnsureCreatedAsync();

        var widget = new Widget(tenantId, "tx-test");
        widget.RaiseTestEvent();
        ctx.Widgets.Add(widget);

        await using (var tx = await ctx.Database.BeginTransactionAsync())
        {
            await ctx.SaveChangesAsync();
            await tx.RollbackAsync();
        }

        // Re-open a fresh context against the same connection so we observe
        // committed-only state (the original ctx still has the entities tracked).
        await using var verify = NewContext();
        var outboxRows = await verify.Outbox.CountAsync();
        outboxRows.Should().Be(0);
    }

    private TestDbContext NewContext()
    {
        var options = new DbContextOptionsBuilder<TestDbContext>()
            .UseSqlite(_connection)
            .AddInterceptors(new OutboxInterceptor())
            .Options;
        return new TestDbContext(options);
    }
}
