using System.Security.Claims;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using ShopFlow.SharedKernel.Application;
using ShopFlow.SharedKernel.Application.Ports;
using ShopFlow.SharedKernel.Domain;
using ShopFlow.SharedKernel.Infrastructure.SignalR;

namespace ShopFlow.SharedKernel.UnitTests.SignalR;

/// <summary>
/// Sprint-7 plan U5 — verifies <see cref="TenantBindingHubFilter"/> behaves
/// like <c>TenantRoutingMiddleware</c> for the SignalR transport: extracts
/// the <c>tenant_slug</c> claim, looks up the tenant via
/// <see cref="ITenantCatalog"/>, aborts on any failure mode, joins the
/// <c>tenant:{slug}</c> group on connect, and binds
/// <see cref="RequestContext"/> on each method invocation.
/// </summary>
public sealed class TenantBindingHubFilterTests
{
    private const string ValidSlug = "yensaokhanhhoa";

    private static TenantInfo SampleTenant(
        TenantStatus status = TenantStatus.Ready,
        string slug = ValidSlug
    ) =>
        new(
            Id: Guid.Parse("11111111-1111-1111-1111-111111111111"),
            Slug: slug,
            DbName: $"shopflow_t_{slug}",
            DbConnectionString: $"Host=pgbouncer;Database=shopflow_t_{slug};Username=app;Password=test",
            Region: "ap-southeast-1",
            Tier: "free",
            Status: status
        );

    private static ClaimsPrincipal PrincipalWithSlug(string? slug)
    {
        var identity = new ClaimsIdentity(authenticationType: "test");
        if (slug is not null)
        {
            identity.AddClaim(new Claim(TenantBindingHubFilter.JwtTenantClaim, slug));
        }
        return new ClaimsPrincipal(identity);
    }

    private static HubCallerContext BuildCallerContext(ClaimsPrincipal user, string connectionId)
    {
        var caller = Substitute.For<HubCallerContext>();
        caller.User.Returns(user);
        caller.ConnectionId.Returns(connectionId);
        caller.ConnectionAborted.Returns(CancellationToken.None);
        return caller;
    }

    private static (IServiceScopeFactory scopeFactory, RequestContext requestContext) BuildScopeFactoryWithRequestContext()
    {
        // Single shared RequestContext instance returned by the scope so
        // the test can assert what the filter bound after the call.
        var requestContext = new RequestContext();
        var services = new ServiceCollection();
        services.AddScoped<RequestContext>(_ => requestContext);
        var provider = services.BuildServiceProvider();
        return (provider.GetRequiredService<IServiceScopeFactory>(), requestContext);
    }

    private static (IGroupManager groups, List<(string ConnectionId, string GroupName)> calls) BuildGroupManager()
    {
        var calls = new List<(string, string)>();
        var groups = Substitute.For<IGroupManager>();
        groups
            .AddToGroupAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                calls.Add(((string)callInfo[0], (string)callInfo[1]));
                return Task.CompletedTask;
            });
        return (groups, calls);
    }

    private static TenantHub BuildHub(IGroupManager groups)
    {
        return new TenantHub
        {
            Groups = groups,
        };
    }

    private static HubLifetimeContext BuildLifetimeContext(
        HubCallerContext caller,
        TenantHub hub,
        IServiceProvider sp
    ) => new HubLifetimeContext(caller, sp, hub);

    private static HubInvocationContext BuildInvocationContext(
        HubCallerContext caller,
        TenantHub hub,
        IServiceProvider sp
    )
    {
        // Use a placeholder MethodInfo — Object.ToString() is safe; the
        // filter never invokes it (next() is mocked by the test caller).
        var method = typeof(object).GetMethod(nameof(object.ToString))!;
        return new HubInvocationContext(
            caller,
            sp,
            hub,
            method,
            Array.Empty<object?>()
        );
    }

    [Fact]
    public async Task OnConnected_HappyPath_JoinsTenantGroup()
    {
        // Arrange
        var catalog = Substitute.For<ITenantCatalog>();
        catalog
            .LookupBySlugAsync(ValidSlug, Arg.Any<CancellationToken>())
            .Returns(SampleTenant());

        var (scopeFactory, _) = BuildScopeFactoryWithRequestContext();
        var (groups, joinCalls) = BuildGroupManager();
        var hub = BuildHub(groups);
        var caller = BuildCallerContext(PrincipalWithSlug(ValidSlug), connectionId: "conn-1");
        var serviceProvider = new ServiceCollection().BuildServiceProvider();
        var ctx = BuildLifetimeContext(caller, hub, serviceProvider);

        var filter = new TenantBindingHubFilter(
            scopeFactory,
            catalog,
            NullLogger<TenantBindingHubFilter>.Instance
        );

        var nextCalled = false;

        // Act
        await filter.OnConnectedAsync(ctx, _ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        });

        // Assert
        nextCalled.Should().BeTrue();
        joinCalls.Should().ContainSingle()
            .Which.Should().Be(("conn-1", $"tenant:{ValidSlug}"));
        caller.DidNotReceive().Abort();
    }

    [Fact]
    public async Task OnConnected_MissingClaim_AbortsConnection()
    {
        // Arrange
        var catalog = Substitute.For<ITenantCatalog>();
        var (scopeFactory, _) = BuildScopeFactoryWithRequestContext();
        var (groups, joinCalls) = BuildGroupManager();
        var hub = BuildHub(groups);
        var caller = BuildCallerContext(PrincipalWithSlug(slug: null), connectionId: "conn-1");
        var ctx = BuildLifetimeContext(caller, hub, new ServiceCollection().BuildServiceProvider());

        var filter = new TenantBindingHubFilter(
            scopeFactory,
            catalog,
            NullLogger<TenantBindingHubFilter>.Instance
        );

        var nextCalled = false;

        // Act
        await filter.OnConnectedAsync(ctx, _ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        });

        // Assert
        nextCalled.Should().BeFalse();
        caller.Received(1).Abort();
        joinCalls.Should().BeEmpty();
        await catalog
            .DidNotReceive()
            .LookupBySlugAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task OnConnected_UnknownSlug_AbortsConnection()
    {
        // Arrange
        var catalog = Substitute.For<ITenantCatalog>();
        catalog
            .LookupBySlugAsync("ghost", Arg.Any<CancellationToken>())
            .Returns((TenantInfo?)null);

        var (scopeFactory, _) = BuildScopeFactoryWithRequestContext();
        var (groups, joinCalls) = BuildGroupManager();
        var hub = BuildHub(groups);
        var caller = BuildCallerContext(PrincipalWithSlug("ghost"), connectionId: "conn-1");
        var ctx = BuildLifetimeContext(caller, hub, new ServiceCollection().BuildServiceProvider());

        var filter = new TenantBindingHubFilter(
            scopeFactory,
            catalog,
            NullLogger<TenantBindingHubFilter>.Instance
        );

        var nextCalled = false;

        // Act
        await filter.OnConnectedAsync(ctx, _ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        });

        // Assert
        nextCalled.Should().BeFalse();
        caller.Received(1).Abort();
        joinCalls.Should().BeEmpty();
    }

    [Fact]
    public async Task OnConnected_TenantNotReady_AbortsConnection()
    {
        // Arrange
        var catalog = Substitute.For<ITenantCatalog>();
        catalog
            .LookupBySlugAsync(ValidSlug, Arg.Any<CancellationToken>())
            .Returns(SampleTenant(status: TenantStatus.Provisioning));

        var (scopeFactory, _) = BuildScopeFactoryWithRequestContext();
        var (groups, joinCalls) = BuildGroupManager();
        var hub = BuildHub(groups);
        var caller = BuildCallerContext(PrincipalWithSlug(ValidSlug), connectionId: "conn-1");
        var ctx = BuildLifetimeContext(caller, hub, new ServiceCollection().BuildServiceProvider());

        var filter = new TenantBindingHubFilter(
            scopeFactory,
            catalog,
            NullLogger<TenantBindingHubFilter>.Instance
        );

        var nextCalled = false;

        // Act
        await filter.OnConnectedAsync(ctx, _ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        });

        // Assert
        nextCalled.Should().BeFalse();
        caller.Received(1).Abort();
        joinCalls.Should().BeEmpty();
    }

    [Fact]
    public async Task InvokeMethodAsync_HappyPath_BindsRequestContextBeforeNext()
    {
        // Arrange
        var catalog = Substitute.For<ITenantCatalog>();
        catalog
            .LookupBySlugAsync(ValidSlug, Arg.Any<CancellationToken>())
            .Returns(SampleTenant());

        var (scopeFactory, requestContext) = BuildScopeFactoryWithRequestContext();
        var (groups, _) = BuildGroupManager();
        var hub = BuildHub(groups);
        var caller = BuildCallerContext(PrincipalWithSlug(ValidSlug), connectionId: "conn-1");
        var ctx = BuildInvocationContext(caller, hub, new ServiceCollection().BuildServiceProvider());

        var filter = new TenantBindingHubFilter(
            scopeFactory,
            catalog,
            NullLogger<TenantBindingHubFilter>.Instance
        );

        Guid? observedTenantIdInsideNext = null;
        string? observedSlugInsideNext = null;

        // Act
        var result = await filter.InvokeMethodAsync(ctx, _ =>
        {
            // RequestContext must be bound by the time next runs.
            observedTenantIdInsideNext = requestContext.TenantId;
            observedSlugInsideNext = requestContext.TenantSlug;
            return ValueTask.FromResult<object?>("ok");
        });

        // Assert
        result.Should().Be("ok");
        observedTenantIdInsideNext.Should().Be(SampleTenant().Id);
        observedSlugInsideNext.Should().Be(ValidSlug);
        caller.DidNotReceive().Abort();
    }
}
