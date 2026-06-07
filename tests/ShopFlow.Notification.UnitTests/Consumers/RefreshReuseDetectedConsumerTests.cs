using MassTransit;
using MassTransit.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using ShopFlow.Auth.Application.Ports;
using ShopFlow.Auth.Domain;
using ShopFlow.Auth.Domain.Entities;
using ShopFlow.Contracts.Auth;
using ShopFlow.Notification.Application.Ports;
using ShopFlow.Notification.Domain.Entities;
using ShopFlow.Notification.Infrastructure.Consumers;
using ShopFlow.Notification.Infrastructure.Templates;

namespace ShopFlow.Notification.UnitTests.Consumers;

public sealed class RefreshReuseDetectedConsumerTests
{
    private static readonly Guid AnyTenant = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid AnyUser = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid AnyCorrelation = Guid.Parse(
        "33333333-3333-3333-3333-333333333333"
    );
    private const string DummyHash = "$argon2id$v=19$m=65536,t=4,p=4$ZHVtbXk$ZHVtbXk";

    private static RefreshReuseDetectedV1 NewMessage() =>
        new(
            TenantId: AnyTenant,
            UserId: AnyUser,
            AffectedUserEmail: "alice@example.com",
            ChainId: Guid.Parse("44444444-4444-4444-4444-444444444444"),
            PresentedTokenHash: "abc",
            PresentingIp: "1.2.3.4",
            UserAgent: "Mozilla/5.0",
            OccurredAtUtc: new DateTime(2026, 5, 21, 10, 0, 0, DateTimeKind.Utc),
            CorrelationId: AnyCorrelation
        );

    [Fact]
    public async Task Consume_TenantWith3Owners_InsertsThreeOutboxRows()
    {
        var users = Substitute.For<IUserRepository>();
        users
            .ListByRoleAsync(UserRole.Owner, Arg.Any<CancellationToken>())
            .Returns(
                new List<User>
                {
                    User.Create("owner1@tenant-a.com", DummyHash, UserRole.Owner),
                    User.Create("owner2@tenant-a.com", DummyHash, UserRole.Owner),
                    User.Create("owner3@tenant-a.com", DummyHash, UserRole.Owner),
                }
            );

        var outbox = Substitute.For<INotificationOutboxRepository>();
        outbox
            .InsertAsync(Arg.Any<NotificationOutboxEntry>(), Arg.Any<CancellationToken>())
            .Returns(_ => Guid.NewGuid());

        await using var sp = BuildServiceProvider(outbox, users);
        var harness = sp.GetRequiredService<ITestHarness>();
        await harness.Start();

        await harness.Bus.Publish(NewMessage());

        var consumerHarness = harness.GetConsumerHarness<RefreshReuseDetectedConsumer>();
        (await consumerHarness.Consumed.Any<RefreshReuseDetectedV1>()).Should().BeTrue();

        await outbox
            .Received(3)
            .InsertAsync(
                Arg.Is<NotificationOutboxEntry>(e =>
                    e.NotificationKind == "RefreshReuse"
                    && e.SourceEventId == AnyCorrelation
                    && e.RenderedSubject.Contains("Suspicious", StringComparison.Ordinal)
                ),
                Arg.Any<CancellationToken>()
            );

        await harness.Stop();
    }

    [Fact]
    public async Task Consume_TenantWithZeroOwners_InsertsZeroRows()
    {
        var users = Substitute.For<IUserRepository>();
        users
            .ListByRoleAsync(UserRole.Owner, Arg.Any<CancellationToken>())
            .Returns(new List<User>());

        var outbox = Substitute.For<INotificationOutboxRepository>();

        await using var sp = BuildServiceProvider(outbox, users);
        var harness = sp.GetRequiredService<ITestHarness>();
        await harness.Start();

        await harness.Bus.Publish(NewMessage());

        var consumerHarness = harness.GetConsumerHarness<RefreshReuseDetectedConsumer>();
        (await consumerHarness.Consumed.Any<RefreshReuseDetectedV1>()).Should().BeTrue();

        await outbox
            .DidNotReceive()
            .InsertAsync(Arg.Any<NotificationOutboxEntry>(), Arg.Any<CancellationToken>());

        await harness.Stop();
    }

    [Fact]
    public async Task Consume_InactiveOwnersAreSkipped()
    {
        var activeOwner = User.Create("active@tenant-a.com", DummyHash, UserRole.Owner);
        var inactiveOwner = User.Create("inactive@tenant-a.com", DummyHash, UserRole.Owner);
        inactiveOwner.Deactivate();

        var users = Substitute.For<IUserRepository>();
        users
            .ListByRoleAsync(UserRole.Owner, Arg.Any<CancellationToken>())
            .Returns(new List<User> { activeOwner, inactiveOwner });

        var outbox = Substitute.For<INotificationOutboxRepository>();
        outbox
            .InsertAsync(Arg.Any<NotificationOutboxEntry>(), Arg.Any<CancellationToken>())
            .Returns(_ => Guid.NewGuid());

        await using var sp = BuildServiceProvider(outbox, users);
        var harness = sp.GetRequiredService<ITestHarness>();
        await harness.Start();

        await harness.Bus.Publish(NewMessage());

        var consumerHarness = harness.GetConsumerHarness<RefreshReuseDetectedConsumer>();
        (await consumerHarness.Consumed.Any<RefreshReuseDetectedV1>()).Should().BeTrue();

        await outbox
            .Received(1)
            .InsertAsync(
                Arg.Is<NotificationOutboxEntry>(e => e.RecipientEmail == "active@tenant-a.com"),
                Arg.Any<CancellationToken>()
            );
        await outbox
            .DidNotReceive()
            .InsertAsync(
                Arg.Is<NotificationOutboxEntry>(e => e.RecipientEmail == "inactive@tenant-a.com"),
                Arg.Any<CancellationToken>()
            );

        await harness.Stop();
    }

    private static ServiceProvider BuildServiceProvider(
        INotificationOutboxRepository outbox,
        IUserRepository users
    )
    {
        var services = new ServiceCollection();
        services.AddSingleton(outbox);
        services.AddSingleton(users);
        services.AddSingleton<ITemplateRenderer, SimpleTemplateRenderer>();
        services.AddSingleton<TemplateResourceLoader>();
        services.AddLogging(b => b.AddProvider(NullLoggerProvider.Instance));
        services.AddMassTransitTestHarness(cfg => cfg.AddConsumer<RefreshReuseDetectedConsumer>());
        return services.BuildServiceProvider(validateScopes: true);
    }
}
