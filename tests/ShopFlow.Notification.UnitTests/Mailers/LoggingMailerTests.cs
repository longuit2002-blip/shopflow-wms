using Microsoft.Extensions.Logging.Abstractions;
using ShopFlow.Notification.Domain.ValueObjects;
using ShopFlow.Notification.Infrastructure.Mailers;

namespace ShopFlow.Notification.UnitTests.Mailers;

public sealed class LoggingMailerTests
{
    private static readonly Guid AnyTenant = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid AnyEvent = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private readonly LoggingMailer _mailer = new(NullLogger<LoggingMailer>.Instance);

    [Fact]
    public async Task SendAsync_ReturnsSuccessWithSyntheticMessageId()
    {
        var email = RenderedEmail.Create("Subject", "body", "<p>body</p>", AnyEvent);
        var recipient = Recipient.Create("alice@example.com", "Alice", AnyTenant);

        var result = await _mailer.SendAsync(email, recipient, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result
            .Value.Value.Should()
            .Match("<dev-*@logging-mailer.local>");
    }

    [Fact]
    public async Task SendAsync_GeneratesDifferentMessageIdsAcrossCalls()
    {
        var email = RenderedEmail.Create("Subject", "body", "<p/>", AnyEvent);
        var recipient = Recipient.Create("alice@example.com", "Alice", AnyTenant);

        var r1 = await _mailer.SendAsync(email, recipient, CancellationToken.None);
        var r2 = await _mailer.SendAsync(email, recipient, CancellationToken.None);

        r1.Value.Value.Should().NotBe(r2.Value.Value);
    }

    [Fact]
    public async Task SendAsync_NullEmail_ThrowsArgumentNullException()
    {
        var recipient = Recipient.Create("alice@example.com", "Alice", AnyTenant);

        var act = async () =>
            await _mailer.SendAsync(null!, recipient, CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task SendAsync_NullRecipient_ThrowsArgumentNullException()
    {
        var email = RenderedEmail.Create("Subject", "body", "<p/>", AnyEvent);

        var act = async () => await _mailer.SendAsync(email, null!, CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentNullException>();
    }
}
