using MassTransit;
using Microsoft.Extensions.Logging;
using ShopFlow.Auth.Application.Ports;
using ShopFlow.Auth.Domain;
using ShopFlow.Contracts.Auth;
using ShopFlow.Notification.Application.Ports;
using ShopFlow.Notification.Domain.Entities;
using ShopFlow.Notification.Domain.ValueObjects;
using ShopFlow.Notification.Infrastructure.Templates;

namespace ShopFlow.Notification.Infrastructure.Consumers;

/// <summary>
/// Sprint-9.5 U3 — consumes <see cref="RefreshReuseDetectedV1"/> and
/// fans out the Owner alert to every <see cref="UserRole.Owner"/>
/// user for the tenant (KTD15 origin R28 — Sprint-9.5 ships Owner-only;
/// Sprint-10+ stretch may also email the affected user themselves per
/// OWASP Session Management Cheat Sheet).
/// </summary>
public sealed class RefreshReuseDetectedConsumer : IConsumer<RefreshReuseDetectedV1>
{
    private readonly INotificationOutboxRepository _outbox;
    private readonly IUserRepository _users;
    private readonly ITemplateRenderer _renderer;
    private readonly TemplateResourceLoader _templates;
    private readonly ILogger<RefreshReuseDetectedConsumer> _logger;

    public RefreshReuseDetectedConsumer(
        INotificationOutboxRepository outbox,
        IUserRepository users,
        ITemplateRenderer renderer,
        TemplateResourceLoader templates,
        ILogger<RefreshReuseDetectedConsumer> logger
    )
    {
        _outbox = outbox;
        _users = users;
        _renderer = renderer;
        _templates = templates;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<RefreshReuseDetectedV1> context)
    {
        var msg = context.Message;
        var ct = context.CancellationToken;

        var owners = await _users.ListByRoleAsync(UserRole.Owner, ct).ConfigureAwait(false);
        if (owners.Count == 0)
        {
            _logger.LogDebug(
                "RefreshReuseDetectedV1 consumed for tenant {TenantId} but no Owner-role users exist; no email fan-out",
                msg.TenantId
            );
            return;
        }

        var vars = new Dictionary<string, string>
        {
            ["affected_user_email"] = msg.AffectedUserEmail ?? "(unknown)",
            ["presenting_ip"] = msg.PresentingIp ?? "(unknown)",
            ["user_agent"] = msg.UserAgent ?? "(unknown)",
            ["occurred_at_utc"] = msg.OccurredAtUtc.ToString("u"),
            ["chain_id"] = msg.ChainId.ToString(),
        };

        var subject = $"Suspicious activity on account {msg.AffectedUserEmail}";
        var textBody = _renderer.RenderText(_templates.Load("refresh-reuse.txt"), vars);
        var htmlBody = _renderer.RenderHtml(_templates.Load("refresh-reuse.html"), vars);

        foreach (var owner in owners)
        {
            if (!owner.IsActive)
            {
                continue;
            }

            var row = new NotificationOutboxEntry
            {
                SourceEventId = msg.CorrelationId,
                NotificationKind = NotificationKind.RefreshReuse.ToString(),
                RecipientEmail = owner.Email.Trim().ToLowerInvariant(),
                RecipientDisplayName = null,
                RenderedSubject = subject,
                RenderedBodyText = textBody,
                RenderedBodyHtml = htmlBody,
                Status = "pending",
            };

            await _outbox.InsertAsync(row, ct).ConfigureAwait(false);
        }

        _logger.LogInformation(
            "RefreshReuseDetectedV1 consumed: tenant={TenantId} affected_user={Affected} owners_notified={Count}",
            msg.TenantId,
            msg.AffectedUserEmail,
            owners.Count(o => o.IsActive)
        );
    }
}
