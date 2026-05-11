using ShopFlow.SharedKernel.Application;
using ShopFlow.SharedKernel.Application.Ports;

namespace ShopFlow.SharedKernel.UnitTests.Application;

public class RequestContextTests
{
    private static TenantInfo SampleTenant(string slug = "acme") =>
        new(
            Id: Guid.Parse("11111111-1111-1111-1111-111111111111"),
            Slug: slug,
            DbName: $"shopflow_t_{slug}",
            DbConnectionString: $"Host=pgbouncer;Database=shopflow_t_{slug};Username=app;Password=test",
            Region: "ap-southeast-1",
            Tier: "free",
            Status: TenantStatus.Ready
        );

    [Fact]
    public void TenantId_throws_before_bind()
    {
        var ctx = new RequestContext();

        var act = () => _ = ctx.TenantId;

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Bind_populates_tenant_fields_and_correlation_id()
    {
        var ctx = new RequestContext();
        var tenant = SampleTenant();

        ctx.Bind(tenant, "corr-xyz", userId: null);

        ctx.TenantId.Should().Be(tenant.Id);
        ctx.TenantSlug.Should().Be(tenant.Slug);
        ctx.DbConnectionString.Should().Be(tenant.DbConnectionString);
        ctx.CorrelationId.Should().Be("corr-xyz");
        ctx.UserId.Should().BeNull();
    }

    [Fact]
    public void Bind_throws_on_null_correlation_id()
    {
        var ctx = new RequestContext();
        var tenant = SampleTenant();

        var act = () => ctx.Bind(tenant, correlationId: null!, userId: null);

        act.Should().Throw<ArgumentNullException>();
    }
}
