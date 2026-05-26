using Microsoft.Extensions.DependencyInjection;
using ShopFlow.SharedKernel.Infrastructure;

namespace ShopFlow.SharedKernel.UnitTests;

/// <summary>
/// Sprint-4 plan U4 (K13 close) — registry coverage. Pins the
/// publish-by-default behaviour so the Sprint-1/2/3 paths are guaranteed
/// unchanged. The dispatcher-level integration (real Publish vs Send via
/// MT TestHarness) lives in the integration suite (deferred to CI).
/// </summary>
public sealed class OutboxRouteRegistryTests
{
    private sealed record FooMessage;

    private sealed record BarCommand;

    private sealed record UnregisteredEvent;

    [Fact]
    public void Resolve_Unregistered_ReturnsPublishDefault()
    {
        var registry = new OutboxRouteRegistry();

        var route = registry.Resolve(typeof(UnregisteredEvent));

        route.Should().BeSameAs(OutboxRoute.PublishDefault);
        route.Kind.Should().Be(SendKind.Publish);
    }

    [Fact]
    public void Resolve_Registered_ReturnsConfigured()
    {
        var registry = new OutboxRouteRegistry();
        registry.Register(typeof(BarCommand), new OutboxRoute(SendKind.Send));

        var route = registry.Resolve(typeof(BarCommand));

        route.Kind.Should().Be(SendKind.Send);
    }

    [Fact]
    public void Register_LastWriteWins()
    {
        var registry = new OutboxRouteRegistry();
        registry.Register(typeof(BarCommand), new OutboxRoute(SendKind.Publish));
        registry.Register(typeof(BarCommand), new OutboxRoute(SendKind.Send, RoutingKey: "custom"));

        var route = registry.Resolve(typeof(BarCommand));

        route.Kind.Should().Be(SendKind.Send);
        route.RoutingKey.Should().Be("custom");
    }

    [Fact]
    public void Resolve_NullType_Throws()
    {
        var registry = new OutboxRouteRegistry();
        var act = () => registry.Resolve(null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void SeedConstructor_AppliesAllRegistrations()
    {
        var seeds = new[]
        {
            new OutboxRouteSeed(typeof(FooMessage), new OutboxRoute(SendKind.Publish)),
            new OutboxRouteSeed(
                typeof(BarCommand),
                new OutboxRoute(SendKind.Send, RoutingKey: "bar-queue")
            ),
        };

        var registry = new OutboxRouteRegistry(seeds);

        registry.Resolve(typeof(FooMessage)).Kind.Should().Be(SendKind.Publish);
        registry.Resolve(typeof(BarCommand)).Kind.Should().Be(SendKind.Send);
        registry.Resolve(typeof(BarCommand)).RoutingKey.Should().Be("bar-queue");
    }

    [Fact]
    public void SeedConstructor_LastSeedWinsOnConflict()
    {
        var seeds = new[]
        {
            new OutboxRouteSeed(typeof(BarCommand), new OutboxRoute(SendKind.Publish)),
            new OutboxRouteSeed(typeof(BarCommand), new OutboxRoute(SendKind.Send)),
        };

        var registry = new OutboxRouteRegistry(seeds);

        registry.Resolve(typeof(BarCommand)).Kind.Should().Be(SendKind.Send);
    }

    [Fact]
    public void AddOutboxRoute_DiIntegration_RegistersAndResolves()
    {
        // Realistic composition path: AddOutboxRoute<T> seeds the registry,
        // BuildServiceProvider constructs the singleton from the seed ctor.
        var services = new ServiceCollection();
        services.AddOutboxRoute<BarCommand>(SendKind.Send, destination: "bar-q");
        services.AddOutboxRoute<FooMessage>(SendKind.Publish);

        using var provider = services.BuildServiceProvider();
        var registry = provider.GetRequiredService<IOutboxRouteRegistry>();

        registry.Resolve(typeof(BarCommand)).Kind.Should().Be(SendKind.Send);
        registry.Resolve(typeof(BarCommand)).RoutingKey.Should().Be("bar-q");
        registry.Resolve(typeof(FooMessage)).Kind.Should().Be(SendKind.Publish);
        registry.Resolve(typeof(UnregisteredEvent)).Should().BeSameAs(OutboxRoute.PublishDefault);
    }

    [Fact]
    public void AddOutboxRoute_RegistersIOutboxRouteRegistry_AsSingleton()
    {
        var services = new ServiceCollection();
        services.AddOutboxRoute<FooMessage>(SendKind.Publish);

        using var provider = services.BuildServiceProvider();
        var first = provider.GetRequiredService<IOutboxRouteRegistry>();
        var second = provider.GetRequiredService<IOutboxRouteRegistry>();

        first.Should().BeSameAs(second);
    }
}
