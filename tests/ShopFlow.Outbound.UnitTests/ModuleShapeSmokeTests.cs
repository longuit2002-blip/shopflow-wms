using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using ShopFlow.Outbound.Infrastructure;
using ShopFlow.SharedKernel.Infrastructure;

namespace ShopFlow.Outbound.UnitTests;

/// <summary>
/// Smoke test guarding the Outbound module shape after Sprint-3-redux U1
/// replaces the U9 stubs. Asserts the composition entry point exposes its
/// <c>ModuleName</c> constant per AGENTS.md §11.79 and that
/// <c>AddOutboundModule</c> registers <see cref="OutboundDbContext"/> +
/// the <see cref="MultiplexedOutboxDispatcher{TContext}"/> hosted service.
/// Real Domain + Application behavior tests live in
/// <c>tests/ShopFlow.Outbound.UnitTests/Domain/</c> +
/// <c>tests/ShopFlow.Outbound.IntegrationTests/</c> as they land per
/// Sprint-3-redux U2-U9.
/// </summary>
public sealed class ModuleShapeSmokeTests
{
    [Fact]
    public void OutboundServiceCollectionExtensions_ExposesExpectedModuleName()
    {
        OutboundServiceCollectionExtensions.ModuleName.Should().Be("Outbound");
    }

    [Fact]
    public void AddOutboundModule_RegistersDbContextAndOutboxDispatcher()
    {
        var services = new ServiceCollection();
        IConfiguration configuration = new ConfigurationBuilder().Build();

        services.AddOutboundModule(configuration);

        services
            .Should()
            .Contain(d => d.ServiceType == typeof(OutboundDbContext))
            .Which.Lifetime.Should()
            .Be(
                ServiceLifetime.Scoped,
                "the Outbound DbContext is bound per-request from IRequestContext.DbConnectionString"
            );

        services
            .Should()
            .Contain(
                d =>
                    d.ServiceType == typeof(IHostedService)
                    && d.ImplementationType
                        == typeof(MultiplexedOutboxDispatcher<OutboundDbContext>),
                "the outbox dispatcher hosted service must be wired so outbound_outbox_messages drains to the bus"
            );
    }
}
