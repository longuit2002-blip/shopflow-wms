using MailKit.Net.Smtp;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using ShopFlow.Notification.Domain.ValueObjects;
using ShopFlow.Notification.Infrastructure.Mailers;

namespace ShopFlow.Notification.UnitTests.Mailers;

/// <summary>
/// MailKitSmtpMailer happy-path + connection-failure scenarios are
/// covered by U4's Testcontainers Mailpit integration tests. Here we
/// validate the error-shape contract through SmtpResponseCodeMapper
/// (already covered) plus a smoke test on the mailer instantiating
/// against a configured IOptions, since composition root wiring
/// regresses easily.
/// </summary>
public sealed class MailKitSmtpMailerTests
{
    private static readonly Guid AnyTenant = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid AnyEvent = Guid.Parse("22222222-2222-2222-2222-222222222222");

    [Fact]
    public void Constructor_BindsOptionsAndDependencies_WithoutThrowing()
    {
        var options = Options.Create(
            new MailerOptions
            {
                Provider = MailerProviderKind.MailKitSmtp,
                MailKitSmtp = new MailKitSmtpOptions
                {
                    Host = "localhost",
                    Port = 1025,
                    FromEmail = "noreply@example.com",
                    FromDisplayName = "Test",
                },
            }
        );

        var mailer = new MailKitSmtpMailer(
            options,
            new SmtpResponseCodeMapper(),
            NullLogger<MailKitSmtpMailer>.Instance
        );

        mailer.Should().NotBeNull();
    }

    [Fact]
    public async Task SendAsync_ConnectionRefused_ReturnsTransientConnectionError()
    {
        var options = Options.Create(
            new MailerOptions
            {
                Provider = MailerProviderKind.MailKitSmtp,
                MailKitSmtp = new MailKitSmtpOptions
                {
                    // 127.0.0.1:1 is a reserved/closed port — SocketException
                    // on connect path triggers the transient.connection branch.
                    Host = "127.0.0.1",
                    Port = 1,
                    FromEmail = "noreply@example.com",
                    FromDisplayName = "Test",
                },
            }
        );

        var mailer = new MailKitSmtpMailer(
            options,
            new SmtpResponseCodeMapper(),
            NullLogger<MailKitSmtpMailer>.Instance
        );
        var email = RenderedEmail.Create("Subject", "body", "<p/>", AnyEvent);
        var recipient = Recipient.Create("alice@example.com", "Alice", AnyTenant);

        var result = await mailer.SendAsync(email, recipient, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be("mailer.transient.connection");
    }

    [Fact]
    public void SmtpStatusCode535_MapsToPermanent_ViaMapper()
    {
        // Authentication-style 5xx tests the mapper path the
        // MailKitSmtpMailer relies on for permanent classification.
        var mapper = new SmtpResponseCodeMapper();
        var ex = new SmtpCommandException(
            SmtpErrorCode.UnexpectedStatusCode,
            (SmtpStatusCode)535,
            "Authentication credentials invalid"
        );

        var (code, _) = mapper.Map(ex);

        code.Should().Be("mailer.permanent.smtp_5xx");
    }
}
