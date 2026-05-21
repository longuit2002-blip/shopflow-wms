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

public sealed class AccountLockedConsumerTests
{
    private static readonly Guid AnyTenant = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid AnyUser = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid AnyCorrelation = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private const string DummyHash =
        "$argon2id$v=19$m=65536,t=4,p=4$ZHVtbXk$ZHVtbXk";

    private static AccountLockedV1 NewMessage() =>
        new(
            TenantId: AnyTenant,
            UserId: AnyUser,
            UserEmail: "alice@example.com",
            FailedLoginCount: 5,
            LockedUntilUtc: new DateTime(2026, 5, 21, 11, 0, 0, DateTimeKind.Utc),
            SourceIp: "9.8.7.6",
            OccurredAtUtc: new DateTime(2026, 5, 21, 10, 0, 0, DateTimeKind.Utc),
            CorrelationId: AnyCorrelation
        );

    [Fact]
    public async Task Consume_TenantWithSingleOwner_InsertsOneOutboxRow()
    {
        var users = Substitute.For<IUserRepository>();
        users.ListByRoleAsync(UserRole.Owner, Arg.Any<CancellationToken>())
            .Returns(new List<User>
            {
                User.Create("owner@tenant-a.com", DummyHash, UserRole.Owner),
            });

        var outbox = Substitute.For<INotificationOutboxRepository>();
        outbox.InsertAsync(Arg.Any<NotificationOutboxEntry>(), Arg.Any<CancellationToken>())
            .Returns(_ => Guid.NewGuid());

        await using var sp = BuildServiceProvider(outbox, users);
        var harness = sp.GetRequiredService<ITestHarness>();
        await harness.Start();

        await harness.Bus.Publish(NewMessage());

        var consumerHarness = harness.GetConsumerHarness<AccountLockedConsumer>();
        (await consumerHarness.Consumed.Any<AccountLockedV1>()).Should().BeTrue();

        await outbox.Received(1)
            .InsertAsync(
                Arg.Is<NotificationOutboxEntry>(e =>
                    e.NotificationKind == "AccountLocked"
                    && e.SourceEventId == AnyCorrelation
                    && e.RecipientEmail == "owner@tenant-a.com"
                    && e.RenderedSubject.Contains("Account locked", StringComparison.Ordinal)
                ),
                Arg.Any<CancellationToken>()
            );

        await harness.Stop();
    }

    [Fact]
    public async Task Consume_TenantWith2Owners_FansOutToBoth()
    {
        var users = Substitute.For<IUserRepository>();
        users.ListByRoleAsync(UserRole.Owner, Arg.Any<CancellationToken>())
            .Returns(new List<User>
            {
                User.Create("owner1@tenant-a.com", DummyHash, UserRole.Owner),
                User.Create("owner2@tenant-a.com", DummyHash, UserRole.Owner),
            });

        var outbox = Substitute.For<INotificationOutboxRepository>();
        outbox.InsertAsync(Arg.Any<NotificationOutboxEntry>(), Arg.Any<CancellationToken>())
            .Returns(_ => Guid.NewGuid());

        await using var sp = BuildServiceProvider(outbox, users);
        var harness = sp.GetRequiredService<ITestHarness>();
        await harness.Start();

        await harness.Bus.Publish(NewMessage());

        var consumerHarness = harness.GetConsumerHarness<AccountLockedConsumer>();
        (await consumerHarness.Consumed.Any<AccountLockedV1>()).Should().BeTrue();

        await outbox.Received(2)
            .InsertAsync(
                Arg.Is<NotificationOutboxEntry>(e => e.NotificationKind == "AccountLocked"),
                Arg.Any<CancellationToken>()
            );

        await harness.Stop();
    }

    [Fact]
    public async Task Consume_NoOwners_InsertsZeroRows()
    {
        var users = Substitute.For<IUserRepository>();
        users.ListByRoleAsync(UserRole.Owner, Arg.Any<CancellationToken>())
            .Returns(new List<User>());

        var outbox = Substitute.For<INotificationOutboxRepository>();

        await using var sp = BuildServiceProvider(outbox, users);
        var harness = sp.GetRequiredService<ITestHarness>();
        await harness.Start();

        await harness.Bus.Publish(NewMessage());

        var consumerHarness = harness.GetConsumerHarness<AccountLockedConsumer>();
        (await consumerHarness.Consumed.Any<AccountLockedV1>()).Should().BeTrue();

        await outbox.DidNotReceive()
            .InsertAsync(Arg.Any<NotificationOutboxEntry>(), Arg.Any<CancellationToken>());

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
        services.AddMassTransitTestHarness(cfg =>
            cfg.AddConsumer<AccountLockedConsumer>()
        );
        return services.BuildServiceProvider(validateScopes: true);
    }
}
