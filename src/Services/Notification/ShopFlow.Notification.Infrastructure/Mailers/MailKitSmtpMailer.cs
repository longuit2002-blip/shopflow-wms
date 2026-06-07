using System.Net.Sockets;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MimeKit;
using ShopFlow.Notification.Application.Ports;
using ShopFlow.Notification.Domain.ValueObjects;
using ShopFlow.SharedKernel.Domain;

namespace ShopFlow.Notification.Infrastructure.Mailers;

/// <summary>
/// Production <see cref="IMailerProvider"/> backed by MailKit. Opens a
/// fresh <see cref="SmtpClient"/> per send (MailKit's client is not safe
/// for concurrent <c>SendAsync</c> calls); connects with opportunistic
/// STARTTLS per <see cref="MailKitSmtpOptions.UseStartTls"/>; SASL PLAIN
/// authentication only when <see cref="MailKitSmtpOptions.Username"/>
/// is non-empty (anonymous auth for Mailpit dev); maps any failure
/// through <see cref="SmtpResponseCodeMapper"/> to a stable transient /
/// permanent error code (KTD4).
/// </summary>
public sealed class MailKitSmtpMailer : IMailerProvider
{
    private readonly IOptions<MailerOptions> _options;
    private readonly SmtpResponseCodeMapper _mapper;
    private readonly ILogger<MailKitSmtpMailer> _logger;

    public MailKitSmtpMailer(
        IOptions<MailerOptions> options,
        SmtpResponseCodeMapper mapper,
        ILogger<MailKitSmtpMailer> logger
    )
    {
        _options = options;
        _mapper = mapper;
        _logger = logger;
    }

    public async Task<Result<MessageId>> SendAsync(
        RenderedEmail email,
        Recipient recipient,
        CancellationToken ct
    )
    {
        ArgumentNullException.ThrowIfNull(email);
        ArgumentNullException.ThrowIfNull(recipient);

        var smtp = _options.Value.MailKitSmtp;
        var message = BuildMessage(email, recipient, smtp);

        using var client = new SmtpClient();
        try
        {
            var secureOptions = smtp.UseStartTls
                ? SecureSocketOptions.StartTls
                : SecureSocketOptions.None;

            await client
                .ConnectAsync(smtp.Host, smtp.Port, secureOptions, ct)
                .ConfigureAwait(false);

            if (!string.IsNullOrEmpty(smtp.Username))
            {
                await client
                    .AuthenticateAsync(smtp.Username, smtp.Password, ct)
                    .ConfigureAwait(false);
            }

            var response = await client.SendAsync(message, ct).ConfigureAwait(false);
            await client.DisconnectAsync(quit: true, ct).ConfigureAwait(false);

            return Result<MessageId>.Success(new MessageId(message.MessageId));
        }
        catch (SmtpCommandException ex)
        {
            var (code, msg) = _mapper.Map(ex);
            _logger.LogWarning(
                ex,
                "MailKitSmtpMailer command-level failure code={ErrorCode} status={Status}",
                code,
                (int)ex.StatusCode
            );
            return Result<MessageId>.Failure(msg, code);
        }
        catch (SmtpProtocolException ex)
        {
            _logger.LogWarning(ex, "MailKitSmtpMailer protocol-level failure");
            return Result<MessageId>.Failure(ex.Message, "mailer.transient.protocol");
        }
        catch (AuthenticationException ex)
        {
            // MailKit authentication error — credentials wrong is a
            // permanent failure (no retry helps). Distinguish from a
            // transient SMTP auth glitch.
            _logger.LogWarning(ex, "MailKitSmtpMailer authentication failure");
            return Result<MessageId>.Failure(ex.Message, "mailer.permanent.authentication");
        }
        catch (Exception ex) when (ex is IOException or SocketException)
        {
            _logger.LogWarning(ex, "MailKitSmtpMailer connection-level failure");
            return Result<MessageId>.Failure(ex.Message, "mailer.transient.connection");
        }
    }

    private static MimeMessage BuildMessage(
        RenderedEmail email,
        Recipient recipient,
        MailKitSmtpOptions smtp
    )
    {
        var message = new MimeMessage();
        message.From.Add(new MailboxAddress(smtp.FromDisplayName, smtp.FromEmail));
        message.To.Add(new MailboxAddress(recipient.DisplayName ?? string.Empty, recipient.Email));
        message.Subject = email.Subject;

        var body = new BodyBuilder { TextBody = email.BodyText, HtmlBody = email.BodyHtml };
        message.Body = body.ToMessageBody();

        return message;
    }
}
