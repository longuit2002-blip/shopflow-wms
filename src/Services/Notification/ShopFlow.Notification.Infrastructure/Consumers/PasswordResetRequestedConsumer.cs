using MassTransit;
using Microsoft.Extensions.Logging;
using ShopFlow.Contracts.Auth;
using ShopFlow.Notification.Application.Ports;
using ShopFlow.Notification.Domain.Entities;
using ShopFlow.Notification.Domain.ValueObjects;
using ShopFlow.Notification.Infrastructure.Templates;

namespace ShopFlow.Notification.Infrastructure.Consumers;

/// <summary>
/// Sprint-9.5 U3 — consumes <see cref="PasswordResetRequestedV1"/>
/// published by Auth (Sprint-9 U9). Renders the password-reset
/// template and writes one <c>notification_outbox</c> row addressed
/// to the requesting user (single-recipient — no Owner fan-out for
/// password-reset; the user themselves is the target). The U3
/// background dispatcher picks the row up and ships it via
/// <c>IMailerProvider</c>.
/// </summary>
public sealed class PasswordResetRequestedConsumer : IConsumer<PasswordResetRequestedV1>
{
    private readonly INotificationOutboxRepository _outbox;
    private readonly ITemplateRenderer _renderer;
    private readonly TemplateResourceLoader _templates;
    private readonly ILogger<PasswordResetRequestedConsumer> _logger;

    public PasswordResetRequestedConsumer(
        INotificationOutboxRepository outbox,
        ITemplateRenderer renderer,
        TemplateResourceLoader templates,
        ILogger<PasswordResetRequestedConsumer> logger
    )
    {
        _outbox = outbox;
        _renderer = renderer;
        _templates = templates;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<PasswordResetRequestedV1> context)
    {
        var msg = context.Message;
        var ct = context.CancellationToken;

        if (string.IsNullOrWhiteSpace(msg.UserEmail))
        {
            _logger.LogWarning(
                "PasswordResetRequestedV1 has empty UserEmail (TenantId={TenantId} UserId={UserId}); skipping",
                msg.TenantId,
                msg.UserId
            );
            return;
        }

        var vars = new Dictionary<string, string>
        {
            ["user_email"] = msg.UserEmail,
            ["reset_link_url"] = msg.ResetLinkUrl,
            ["expires_at_utc"] = msg.ExpiresAtUtc.ToString("u"),
        };

        var subject = "Reset your ShopFlow password";
        var textBody = _renderer.RenderText(_templates.Load("password-reset.txt"), vars);
        var htmlBody = _renderer.RenderHtml(_templates.Load("password-reset.html"), vars);

        var row = new NotificationOutboxEntry
        {
            SourceEventId = msg.CorrelationId,
            NotificationKind = NotificationKind.PasswordReset.ToString(),
            RecipientEmail = msg.UserEmail.Trim().ToLowerInvariant(),
            RecipientDisplayName = null,
            RenderedSubject = subject,
            RenderedBodyText = textBody,
            RenderedBodyHtml = htmlBody,
            Status = "pending",
        };

        await _outbox.InsertAsync(row, ct).ConfigureAwait(false);

        _logger.LogInformation(
            "PasswordResetRequestedV1 consumed: tenant={TenantId} user={UserId} outbox_id={OutboxId}",
            msg.TenantId,
            msg.UserId,
            row.Id
        );
    }
}
