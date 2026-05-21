using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ShopFlow.Notification.Application.Ports;
using ShopFlow.Notification.Domain.Entities;
using ShopFlow.Notification.Domain.ValueObjects;
using ShopFlow.Notification.Infrastructure.Mailers;
using ShopFlow.SharedKernel.Application;
using ShopFlow.SharedKernel.Application.Ports;
using ShopFlow.SharedKernel.Infrastructure;

namespace ShopFlow.Notification.Infrastructure.BackgroundServices;

/// <summary>
/// Polls <c>notification_outbox</c> per tenant + dispatches pending
/// rows via <see cref="IMailerProvider.SendAsync"/>. Mirrors the
/// multiplex-per-tenant shape of
/// <c>MultiplexedOutboxDispatcher&lt;TContext&gt;</c> but processes
/// the domain-specific email-queue row (rendered emails) rather than
/// MT-publishable envelopes. The plan's KTD2 reference to "reuses the
/// existing generic dispatcher" was incorrect — the row shapes are
/// not interchangeable (see U3 commit body for the deviation note).
/// </summary>
/// <remarks>
/// <para>Per tick (5 seconds by default — configurable via
/// <see cref="NotificationDispatcherOptions.PollIntervalSeconds"/>):</para>
/// <list type="number">
///   <item><description>Resolve all Ready tenants from
///   <see cref="ITenantCatalog.GetReadyTenantsAsync"/>.</description></item>
///   <item><description>For each tenant, open a scope + bind
///   <see cref="RequestContext"/> to that tenant's id + db.</description></item>
///   <item><description>Claim up to <see cref="NotificationDispatcherOptions.BatchSize"/>
///   <c>pending</c> rows via
///   <see cref="INotificationOutboxRepository.ClaimPendingBatchAsync"/>
///   (FOR UPDATE SKIP LOCKED).</description></item>
///   <item><description>For each row, build a <see cref="Recipient"/>
///   + <see cref="RenderedEmail"/> from the row's persisted columns
///   and call <see cref="IMailerProvider.SendAsync"/>.</description></item>
///   <item><description>On success — INSERT <c>notification_log</c>
///   (KTD3 UNIQUE may fail → silently drop the row at debug); DELETE
///   the outbox row.</description></item>
///   <item><description>On transient failure — bump
///   <c>attempt_count</c> + stamp <c>last_error_code</c>; keep the
///   outbox row. When <c>attempt_count</c> reaches
///   <see cref="NotificationDispatcherOptions.MaxAttempts"/>, treat as
///   terminal: insert <c>notification_dead_letter</c> + delete outbox.</description></item>
///   <item><description>On permanent failure — insert
///   <c>notification_dead_letter</c> + delete outbox (no retries).</description></item>
/// </list>
/// </remarks>
public sealed class NotificationDeliveryDispatcher : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IOptions<NotificationDispatcherOptions> _options;
    private readonly ILogger<NotificationDeliveryDispatcher> _logger;

    public NotificationDeliveryDispatcher(
        IServiceScopeFactory scopeFactory,
        IOptions<NotificationDispatcherOptions> options,
        ILogger<NotificationDeliveryDispatcher> logger
    )
    {
        _scopeFactory = scopeFactory;
        _options = options;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = TimeSpan.FromSeconds(_options.Value.PollIntervalSeconds);
        using var timer = new PeriodicTimer(interval);

        while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
        {
            try
            {
                await DispatchAllTenantsAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "NotificationDeliveryDispatcher tick failed; will retry on next tick"
                );
            }
        }
    }

    private async Task DispatchAllTenantsAsync(CancellationToken ct)
    {
        await using var rootScope = _scopeFactory.CreateAsyncScope();
        var catalog = rootScope.ServiceProvider.GetRequiredService<ITenantCatalog>();
        var tenants = await catalog.GetReadyTenantsAsync(ct).ConfigureAwait(false);

        if (tenants.Count == 0)
        {
            return;
        }

        foreach (var tenant in tenants)
        {
            try
            {
                await DispatchOneTenantAsync(tenant, ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "NotificationDeliveryDispatcher tenant {Slug} ({TenantId}) failed; other tenants continue",
                    tenant.Slug,
                    tenant.Id
                );
            }
        }
    }

    private async Task DispatchOneTenantAsync(TenantInfo tenant, CancellationToken ct)
    {
        await using var tenantScope = _scopeFactory.CreateAsyncScope();
        var requestContext = tenantScope.ServiceProvider.GetRequiredService<RequestContext>();
        requestContext.Bind(tenant, Guid.NewGuid().ToString("N"), userId: null);

        var outboxRepo =
            tenantScope.ServiceProvider.GetRequiredService<INotificationOutboxRepository>();
        var logRepo =
            tenantScope.ServiceProvider.GetRequiredService<INotificationLogRepository>();
        var mailer = tenantScope.ServiceProvider.GetRequiredService<IMailerProvider>();

        var batch = await outboxRepo
            .ClaimPendingBatchAsync(_options.Value.BatchSize, ct)
            .ConfigureAwait(false);
        if (batch.Count == 0)
        {
            return;
        }

        foreach (var row in batch)
        {
            await ProcessRowAsync(row, outboxRepo, logRepo, mailer, tenant, ct)
                .ConfigureAwait(false);
        }
    }

    private async Task ProcessRowAsync(
        NotificationOutboxEntry row,
        INotificationOutboxRepository outboxRepo,
        INotificationLogRepository logRepo,
        IMailerProvider mailer,
        TenantInfo tenant,
        CancellationToken ct
    )
    {
        Recipient recipient;
        RenderedEmail email;
        try
        {
            recipient = Recipient.Create(
                row.RecipientEmail,
                row.RecipientDisplayName,
                tenant.Id
            );
            email = RenderedEmail.Create(
                row.RenderedSubject,
                row.RenderedBodyText,
                row.RenderedBodyHtml,
                row.SourceEventId
            );
        }
        catch (ArgumentException ex)
        {
            // Row failed value-object reconstruction — terminal data
            // corruption. Move to dead-letter and drop.
            await DeadLetterAsync(
                row,
                logRepo,
                outboxRepo,
                attemptCount: row.AttemptCount + 1,
                errorCode: "mailer.permanent.invalid_payload",
                errorMessage: ex.Message,
                ct
            ).ConfigureAwait(false);
            return;
        }

        var result = await mailer.SendAsync(email, recipient, ct).ConfigureAwait(false);

        if (result.IsSuccess)
        {
            var logEntry = new NotificationLogEntry
            {
                SourceEventId = email.SourceEventId,
                RecipientEmail = recipient.Email,
                NotificationKind = row.NotificationKind,
                MessageId = result.Value.Value,
                ProviderResponseCode = "250 OK",
                SentAt = DateTime.UtcNow,
            };

            var inserted = await logRepo
                .TryInsertSuccessAsync(logEntry, ct)
                .ConfigureAwait(false);
            if (!inserted)
            {
                // KTD3 — duplicate row blocked by UNIQUE. The previous
                // sender already delivered; drop the redundant outbox
                // row silently.
                _logger.LogDebug(
                    "Duplicate notification_log INSERT blocked by KTD3 UNIQUE for tenant {Slug} source_event_id {SourceEventId} recipient {Recipient}; dropping redundant outbox row",
                    tenant.Slug,
                    email.SourceEventId,
                    recipient.Email
                );
            }
            await outboxRepo.DeleteAsync(row.Id, ct).ConfigureAwait(false);
            return;
        }

        var errorCode = result.ErrorCode ?? "mailer.permanent.unknown";
        var attemptCount = row.AttemptCount + 1;
        var isPermanent =
            errorCode.StartsWith("mailer.permanent.", StringComparison.Ordinal)
            || attemptCount >= _options.Value.MaxAttempts;

        if (isPermanent)
        {
            await DeadLetterAsync(
                row,
                logRepo,
                outboxRepo,
                attemptCount,
                errorCode,
                result.Error ?? "(no message)",
                ct
            ).ConfigureAwait(false);
        }
        else
        {
            await outboxRepo
                .UpdateAttemptAsync(row.Id, attemptCount, DateTime.UtcNow, errorCode, ct)
                .ConfigureAwait(false);
            _logger.LogInformation(
                "Notification transient failure tenant={Slug} row={RowId} attempt={Attempt}/{Max} code={Code}",
                tenant.Slug,
                row.Id,
                attemptCount,
                _options.Value.MaxAttempts,
                errorCode
            );
        }
    }

    private async Task DeadLetterAsync(
        NotificationOutboxEntry row,
        INotificationLogRepository logRepo,
        INotificationOutboxRepository outboxRepo,
        int attemptCount,
        string errorCode,
        string errorMessage,
        CancellationToken ct
    )
    {
        var payload = JsonSerializer.Serialize(
            new
            {
                row.SourceEventId,
                row.RecipientEmail,
                row.RecipientDisplayName,
                row.NotificationKind,
                row.RenderedSubject,
                row.RenderedBodyText,
                row.RenderedBodyHtml,
            },
            OutboxJsonOptions.Default
        );

        var dlq = new NotificationDeadLetterEntry
        {
            SourceEventId = row.SourceEventId,
            RecipientEmail = row.RecipientEmail,
            NotificationKind = row.NotificationKind,
            PayloadJson = payload,
            AttemptCount = attemptCount,
            LastErrorCode = errorCode,
            LastErrorMessage = errorMessage,
            DeadLetteredAt = DateTime.UtcNow,
        };

        await logRepo.InsertDeadLetterAsync(dlq, ct).ConfigureAwait(false);
        await outboxRepo.DeleteAsync(row.Id, ct).ConfigureAwait(false);

        _logger.LogError(
            "Notification dead-lettered row={RowId} source_event_id={SourceEventId} recipient={Recipient} code={Code} attempts={Attempts}",
            row.Id,
            row.SourceEventId,
            row.RecipientEmail,
            errorCode,
            attemptCount
        );
    }
}

/// <summary>
/// Notification dispatcher tuning. Bound from
/// <c>Notification:Dispatcher</c> in appsettings. Defaults match the
/// "low-throughput, low-latency" character of transactional email —
/// 5-second poll, 50-row batch, 3 retries before dead-letter.
/// </summary>
public sealed class NotificationDispatcherOptions
{
    public const string SectionName = "Notification:Dispatcher";

    public int PollIntervalSeconds { get; set; } = 5;

    public int BatchSize { get; set; } = 50;

    public int MaxAttempts { get; set; } = 3;
}
