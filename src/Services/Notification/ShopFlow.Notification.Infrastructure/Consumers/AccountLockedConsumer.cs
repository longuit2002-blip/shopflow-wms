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
/// Sprint-9.5 U3 — consumes <see cref="AccountLockedV1"/> and fans out
/// the Owner-alert email to every active <see cref="UserRole.Owner"/>
/// user for the tenant (same fan-out shape as
/// <see cref="RefreshReuseDetectedConsumer"/>; differs from the
/// single-recipient password-reset / MFA-enrolled consumers).
/// </summary>
public sealed class AccountLockedConsumer : IConsumer<AccountLockedV1>
{
    private readonly INotificationOutboxRepository _outbox;
    private readonly IUserRepository _users;
    private readonly ITemplateRenderer _renderer;
    private readonly TemplateResourceLoader _templates;
    private readonly ILogger<AccountLockedConsumer> _logger;

    public AccountLockedConsumer(
        INotificationOutboxRepository outbox,
        IUserRepository users,
        ITemplateRenderer renderer,
        TemplateResourceLoader templates,
        ILogger<AccountLockedConsumer> logger
    )
    {
        _outbox = outbox;
        _users = users;
        _renderer = renderer;
        _templates = templates;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<AccountLockedV1> context)
    {
        var msg = context.Message;
        var ct = context.CancellationToken;

        var owners = await _users.ListByRoleAsync(UserRole.Owner, ct).ConfigureAwait(false);
        if (owners.Count == 0)
        {
            _logger.LogDebug(
                "AccountLockedV1 consumed for tenant {TenantId} but no Owner-role users exist; no email fan-out",
                msg.TenantId
            );
            return;
        }

        var vars = new Dictionary<string, string>
        {
            ["user_email"] = msg.UserEmail ?? "(unknown)",
            ["failed_login_count"] = msg.FailedLoginCount.ToString(),
            ["locked_until_utc"] = msg.LockedUntilUtc.ToString("u"),
            ["source_ip"] = msg.SourceIp ?? "(unknown)",
            ["occurred_at_utc"] = msg.OccurredAtUtc.ToString("u"),
        };

        var subject = $"Account locked: {msg.UserEmail}";
        var textBody = _renderer.RenderText(_templates.Load("account-locked.txt"), vars);
        var htmlBody = _renderer.RenderHtml(_templates.Load("account-locked.html"), vars);

        foreach (var owner in owners)
        {
            if (!owner.IsActive)
            {
                continue;
            }

            var row = new NotificationOutboxEntry
            {
                SourceEventId = msg.CorrelationId,
                NotificationKind = NotificationKind.AccountLocked.ToString(),
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
            "AccountLockedV1 consumed: tenant={TenantId} locked_user={User} owners_notified={Count}",
            msg.TenantId,
            msg.UserEmail,
            owners.Count(o => o.IsActive)
        );
    }
}
