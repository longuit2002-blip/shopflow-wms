using Microsoft.Extensions.Logging;
using ShopFlow.Notification.Application.Ports;
using ShopFlow.Notification.Domain.ValueObjects;
using ShopFlow.SharedKernel.Domain;

namespace ShopFlow.Notification.Infrastructure.Mailers;

/// <summary>
/// Dev-safety default <see cref="IMailerProvider"/>. Writes a single
/// structured log line per "send" capturing the rendered subject +
/// recipient + a synthetic <c>Message-Id</c>; always returns
/// <c>Result.Success</c> with that id. Selected at composition when
/// <see cref="MailerOptions.Provider"/> is <see cref="MailerProviderKind.Logging"/>
/// or when <c>MailKitSmtp</c> credentials are unconfigured.
/// </summary>
/// <remarks>
/// <para>The logging mailer is intentionally never failing — its job is
/// to make local development surface "what would have been sent" in
/// log scrapers without needing a live SMTP target. Stage U3 dispatcher
/// will move the outbox row into <c>notification_log</c> as if the
/// send succeeded.</para>
/// <para>Use Mailpit (via <see cref="MailerProviderKind.MailKitSmtp"/>
/// pointed at the Aspire-managed container) when you want to actually
/// inspect rendered HTML or hand-test deliverability flows.</para>
/// </remarks>
public sealed class LoggingMailer : IMailerProvider
{
    private readonly ILogger<LoggingMailer> _logger;

    public LoggingMailer(ILogger<LoggingMailer> logger)
    {
        _logger = logger;
    }

    public Task<Result<MessageId>> SendAsync(
        RenderedEmail email,
        Recipient recipient,
        CancellationToken ct
    )
    {
        ArgumentNullException.ThrowIfNull(email);
        ArgumentNullException.ThrowIfNull(recipient);

        var messageId = new MessageId($"<dev-{Guid.NewGuid():N}@logging-mailer.local>");

        _logger.LogInformation(
            "LoggingMailer dispatched notification — tenant_id={TenantId} recipient={Recipient} subject={Subject} message_id={MessageId} source_event_id={SourceEventId}",
            recipient.TenantId,
            recipient.Email,
            email.Subject,
            messageId.Value,
            email.SourceEventId
        );

        return Task.FromResult(Result<MessageId>.Success(messageId));
    }
}
