using MassTransit;
using MassTransit.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using ShopFlow.Contracts.Auth;
using ShopFlow.Notification.Application.Ports;
using ShopFlow.Notification.Domain.Entities;
using ShopFlow.Notification.Infrastructure.Consumers;
using ShopFlow.Notification.Infrastructure.Templates;

namespace ShopFlow.Notification.UnitTests.Consumers;

public sealed class PasswordResetRequestedConsumerTests
{
    private static readonly Guid AnyTenant = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid AnyUser = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid AnyCorrelation = Guid.Parse("33333333-3333-3333-3333-333333333333");

    private static PasswordResetRequestedV1 NewMessage(string email = "alice@example.com") =>
        new(
            TenantId: AnyTenant,
            UserId: AnyUser,
            UserEmail: email,
            TenantSlug: "tenant-a",
            ResetLinkUrl: "https://tenant-a.shopflow.local/reset?token=abc",
            ExpiresAtUtc: new DateTime(2026, 5, 21, 11, 0, 0, DateTimeKind.Utc),
            OccurredAtUtc: new DateTime(2026, 5, 21, 10, 0, 0, DateTimeKind.Utc),
            CorrelationId: AnyCorrelation
        );

    [Fact]
    public async Task Consume_HappyPath_InsertsSingleOutboxRow()
    {
        var outbox = Substitute.For<INotificationOutboxRepository>();
        outbox.InsertAsync(Arg.Any<NotificationOutboxEntry>(), Arg.Any<CancellationToken>())
            .Returns(_ => Guid.NewGuid());

        await using var sp = BuildServiceProvider(outbox);
        var harness = sp.GetRequiredService<ITestHarness>();
        await harness.Start();

        await harness.Bus.Publish(NewMessage());

        var consumerHarness = harness.GetConsumerHarness<PasswordResetRequestedConsumer>();
        (await consumerHarness.Consumed.Any<PasswordResetRequestedV1>()).Should().BeTrue();

        await outbox.Received(1)
            .InsertAsync(
                Arg.Is<NotificationOutboxEntry>(e =>
                    e.RecipientEmail == "alice@example.com"
                    && e.NotificationKind == "PasswordReset"
                    && e.SourceEventId == AnyCorrelation
                    && e.RenderedSubject.Contains("Reset", StringComparison.Ordinal)
                ),
                Arg.Any<CancellationToken>()
            );

        await harness.Stop();
    }

    [Fact]
    public async Task Consume_EmptyEmail_SkipsInsert()
    {
        var outbox = Substitute.For<INotificationOutboxRepository>();
        await using var sp = BuildServiceProvider(outbox);
        var harness = sp.GetRequiredService<ITestHarness>();
        await harness.Start();

        await harness.Bus.Publish(NewMessage(email: string.Empty));

        var consumerHarness = harness.GetConsumerHarness<PasswordResetRequestedConsumer>();
        (await consumerHarness.Consumed.Any<PasswordResetRequestedV1>()).Should().BeTrue();

        await outbox.DidNotReceive()
            .InsertAsync(Arg.Any<NotificationOutboxEntry>(), Arg.Any<CancellationToken>());

        await harness.Stop();
    }

    [Fact]
    public async Task Consume_LowercasesAndTrimsRecipientEmail()
    {
        var outbox = Substitute.For<INotificationOutboxRepository>();
        outbox.InsertAsync(Arg.Any<NotificationOutboxEntry>(), Arg.Any<CancellationToken>())
            .Returns(_ => Guid.NewGuid());

        await using var sp = BuildServiceProvider(outbox);
        var harness = sp.GetRequiredService<ITestHarness>();
        await harness.Start();

        await harness.Bus.Publish(NewMessage(email: "  ALICE@Example.COM  "));

        var consumerHarness = harness.GetConsumerHarness<PasswordResetRequestedConsumer>();
        (await consumerHarness.Consumed.Any<PasswordResetRequestedV1>()).Should().BeTrue();

        await outbox.Received(1)
            .InsertAsync(
                Arg.Is<NotificationOutboxEntry>(e => e.RecipientEmail == "alice@example.com"),
                Arg.Any<CancellationToken>()
            );

        await harness.Stop();
    }

    private static ServiceProvider BuildServiceProvider(INotificationOutboxRepository outbox)
    {
        var services = new ServiceCollection();
        services.AddSingleton(outbox);
        services.AddSingleton<ITemplateRenderer, SimpleTemplateRenderer>();
        services.AddSingleton<TemplateResourceLoader>();
        services.AddLogging(b => b.AddProvider(NullLoggerProvider.Instance));
        services.AddMassTransitTestHarness(cfg =>
            cfg.AddConsumer<PasswordResetRequestedConsumer>()
        );
        return services.BuildServiceProvider(validateScopes: true);
    }
}
