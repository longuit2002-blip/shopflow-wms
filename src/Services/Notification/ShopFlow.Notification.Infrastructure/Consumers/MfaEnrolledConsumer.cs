using MassTransit;
using Microsoft.Extensions.Logging;
using ShopFlow.Contracts.Auth;
using ShopFlow.Notification.Application.Ports;
using ShopFlow.Notification.Domain.Entities;
using ShopFlow.Notification.Domain.ValueObjects;
using ShopFlow.Notification.Infrastructure.Templates;

namespace ShopFlow.Notification.Infrastructure.Consumers;

/// <summary>
/// Sprint-9.5 U3 — consumes <see cref="MfaEnrolledV1"/>. Sends the
/// "MFA enabled on your account" confirmation to the user themselves
/// (single recipient; differs from the Owner-fan-out shape of
/// chain-reuse + account-locked per origin R28).
/// </summary>
public sealed class MfaEnrolledConsumer : IConsumer<MfaEnrolledV1>
{
    private readonly INotificationOutboxRepository _outbox;
    private readonly ITemplateRenderer _renderer;
    private readonly TemplateResourceLoader _templates;
    private readonly ILogger<MfaEnrolledConsumer> _logger;

    public MfaEnrolledConsumer(
        INotificationOutboxRepository outbox,
        ITemplateRenderer renderer,
        TemplateResourceLoader templates,
        ILogger<MfaEnrolledConsumer> logger
    )
    {
        _outbox = outbox;
        _renderer = renderer;
        _templates = templates;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<MfaEnrolledV1> context)
    {
        var msg = context.Message;
        var ct = context.CancellationToken;

        if (string.IsNullOrWhiteSpace(msg.UserEmail))
        {
            _logger.LogWarning(
                "MfaEnrolledV1 has empty UserEmail (TenantId={TenantId} UserId={UserId}); skipping",
                msg.TenantId,
                msg.UserId
            );
            return;
        }

        var vars = new Dictionary<string, string>
        {
            ["user_email"] = msg.UserEmail,
            ["occurred_at_utc"] = msg.OccurredAtUtc.ToString("u"),
        };

        var subject = "Multi-factor authentication enabled on your ShopFlow account";
        var textBody = _renderer.RenderText(_templates.Load("mfa-enrolled.txt"), vars);
        var htmlBody = _renderer.RenderHtml(_templates.Load("mfa-enrolled.html"), vars);

        var row = new NotificationOutboxEntry
        {
            SourceEventId = msg.CorrelationId,
            NotificationKind = NotificationKind.MfaEnrolled.ToString(),
            RecipientEmail = msg.UserEmail.Trim().ToLowerInvariant(),
            RecipientDisplayName = null,
            RenderedSubject = subject,
            RenderedBodyText = textBody,
            RenderedBodyHtml = htmlBody,
            Status = "pending",
        };

        await _outbox.InsertAsync(row, ct).ConfigureAwait(false);

        _logger.LogInformation(
            "MfaEnrolledV1 consumed: tenant={TenantId} user={UserId} outbox_id={OutboxId}",
            msg.TenantId,
            msg.UserId,
            row.Id
        );
    }
}
