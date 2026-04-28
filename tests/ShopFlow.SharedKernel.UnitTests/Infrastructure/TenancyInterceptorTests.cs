using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using ShopFlow.SharedKernel.Application;
using ShopFlow.SharedKernel.Infrastructure;
using Xunit;

namespace ShopFlow.SharedKernel.UnitTests.Infrastructure;

public class TenancyInterceptorTests : IAsyncLifetime
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
    public async Task SavingChanges_StampsTenantId_OnAddedEntity_FromRequestContext()
    {
        var tenantId = Guid.NewGuid();
        var ctx = NewContext(new StubRequestContext(tenantId));
        await ctx.Database.EnsureCreatedAsync();

        var widget = new WidgetWithoutTenant("widget-A");
        ctx.Widgets.Add(widget);

        await ctx.SaveChangesAsync();

        var stored = await ctx.Widgets.FirstAsync();
        stored.TenantId.Should().Be(tenantId);
    }

    [Fact]
    public async Task SavingChanges_AllowsExplicitMatchingTenantId_OnAdd()
    {
        var tenantId = Guid.NewGuid();
        var ctx = NewContext(new StubRequestContext(tenantId));
        await ctx.Database.EnsureCreatedAsync();

        ctx.Widgets.Add(new Widget(tenantId, "explicit"));

        var act = async () => await ctx.SaveChangesAsync();

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task SavingChanges_BlocksAdd_WithMismatchedTenantId()
    {
        var requestTenant = Guid.NewGuid();
        var foreignTenant = Guid.NewGuid();
        var ctx = NewContext(new StubRequestContext(requestTenant));
        await ctx.Database.EnsureCreatedAsync();

        ctx.Widgets.Add(new Widget(foreignTenant, "cross-tenant"));

        var act = async () => await ctx.SaveChangesAsync();

        await act.Should()
            .ThrowAsync<InvalidOperationException>()
            .WithMessage("*Cross-tenant write blocked*");
    }

    [Fact]
    public async Task SavingChanges_Throws_WhenRequestContextHasNoTenantId()
    {
        var ctx = NewContext(new RequestContext()); // not initialised
        await ctx.Database.EnsureCreatedAsync();

        ctx.Widgets.Add(new WidgetWithoutTenant("orphan"));

        var act = async () => await ctx.SaveChangesAsync();

        await act.Should()
            .ThrowAsync<InvalidOperationException>()
            .WithMessage("*TenantId accessed before*");
    }

    private TestDbContext NewContext(IRequestContext requestContext)
    {
        var options = new DbContextOptionsBuilder<TestDbContext>()
            .UseSqlite(_connection)
            .AddInterceptors(new TenancyInterceptor(requestContext))
            .Options;
        return new TestDbContext(options);
    }

    /// <summary>
    /// A widget constructed without a tenant id, exercising the
    /// "interceptor stamps it from the request context" path.
    /// </summary>
    private sealed class WidgetWithoutTenant : Widget
    {
        public WidgetWithoutTenant(string name)
            : base(Guid.Empty, name) { }
    }

    private sealed class StubRequestContext : IRequestContext
    {
        public StubRequestContext(Guid tenantId)
        {
            TenantId = tenantId;
            CorrelationId = Guid.NewGuid().ToString();
        }

        public Guid TenantId { get; }
        public string CorrelationId { get; }
        public Guid? UserId => null;
    }
}
