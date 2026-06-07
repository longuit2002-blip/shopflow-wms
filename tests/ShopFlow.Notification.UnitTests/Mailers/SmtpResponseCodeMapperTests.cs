using MailKit.Net.Smtp;
using ShopFlow.Notification.Infrastructure.Mailers;

namespace ShopFlow.Notification.UnitTests.Mailers;

public sealed class SmtpResponseCodeMapperTests
{
    [Fact]
    public void Map_4xxStatus_ReturnsTransientCode()
    {
        var mapper = new SmtpResponseCodeMapper();
        var ex = new SmtpCommandException(
            SmtpErrorCode.UnexpectedStatusCode,
            (SmtpStatusCode)421,
            "Service not available, closing transmission channel"
        );

        var (code, message) = mapper.Map(ex);

        code.Should().Be("mailer.transient.smtp_4xx");
        message
            .Should()
            .Contain("Service not available", "the mailer message should pass through unchanged");
    }

    [Fact]
    public void Map_5xxStatus_ReturnsPermanentCode()
    {
        var mapper = new SmtpResponseCodeMapper();
        var ex = new SmtpCommandException(
            SmtpErrorCode.UnexpectedStatusCode,
            (SmtpStatusCode)550,
            "Mailbox unavailable"
        );

        var (code, _) = mapper.Map(ex);

        code.Should().Be("mailer.permanent.smtp_5xx");
    }

    [Fact]
    public void Map_PerProviderOverride_TakesPrecedenceOver4xxDefault()
    {
        var overrides = new Dictionary<int, string> { [552] = "mailer.transient.quota_exceeded" };
        var mapper = new SmtpResponseCodeMapper(overrides);
        var ex = new SmtpCommandException(
            SmtpErrorCode.UnexpectedStatusCode,
            (SmtpStatusCode)552,
            "Quota exceeded (Sendgrid)"
        );

        var (code, _) = mapper.Map(ex);

        code.Should().Be("mailer.transient.quota_exceeded");
    }

    [Fact]
    public void Map_StatusOutside4xx5xx_ReturnsPermanentUnknown()
    {
        var mapper = new SmtpResponseCodeMapper();
        var ex = new SmtpCommandException(
            SmtpErrorCode.UnexpectedStatusCode,
            (SmtpStatusCode)200,
            "huh?"
        );

        var (code, _) = mapper.Map(ex);

        code.Should().Be("mailer.permanent.unknown");
    }

    [Fact]
    public void Map_NullException_ThrowsArgumentNullException()
    {
        var mapper = new SmtpResponseCodeMapper();

        var act = () => mapper.Map(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Constructor_NoOverrides_BehavesLikeEmptyDictionary()
    {
        var mapper = new SmtpResponseCodeMapper(overrides: null);
        var ex = new SmtpCommandException(
            SmtpErrorCode.UnexpectedStatusCode,
            (SmtpStatusCode)421,
            "transient"
        );

        var (code, _) = mapper.Map(ex);

        code.Should().Be("mailer.transient.smtp_4xx");
    }
}
