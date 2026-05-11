using System.Text.Json;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ShopFlow.SharedKernel.Application;
using ShopFlow.SharedKernel.Application.Ports;

namespace ShopFlow.SharedKernel.Infrastructure;

/// <summary>
/// Mode A (polling) outbox dispatcher per Tech Design v3.0 §5.3.
/// A <see cref="BackgroundService"/> wakes every 500 ms, iterates every
/// tenant currently in <see cref="TenantStatus.Ready"/> via
/// <see cref="ITenantCatalog.GetReadyTenantsAsync"/>, and for each tenant
/// opens a scoped <typeparamref name="TContext"/> against the tenant's DB
/// (via <see cref="IDbContextFactory{TContext}"/>), pulls up to
/// <see cref="BatchSize"/> unprocessed rows, publishes each via
/// <see cref="IPublishEndpoint"/> with a <c>tenant_id</c> header on the
/// envelope, and stamps <c>ProcessedAt</c> on success.
/// </summary>
/// <remarks>
/// <para>Per ADR-0003 the dispatcher is multiplexed across tenants on a
/// single instance — the deployment model is "one BackgroundService per
/// module, that BackgroundService visits every tenant DB each tick." This
/// is the v3.0 replacement for the v2.0 single-DB dispatcher.</para>
///
/// <para>Mode B (LISTEN/NOTIFY) and Mode C (Debezium CDC) are post-MVP
/// upgrades; they do not change the row shape or the per-tenant fan-out.
/// Polling is correct, simple, and ships before W6 — it just has a
/// ~500 ms p99 latency floor that small-SaaS scale tolerates.</para>
///
/// <para>Failure isolation: a publish failure on tenant A increments the
/// row's <c>RetryCount</c> and logs the error, but does not stop the
/// dispatcher's iteration to tenant B. A catalog failure ends the tick
/// (logged + retried on the next tick).</para>
/// </remarks>
/// <typeparam name="TContext">
/// The module's DbContext type. The dispatcher resolves a fresh scope per
/// tenant per tick to avoid leaking the change tracker across iterations
/// and to ensure each tenant gets its own connection-string-bound factory.
/// </typeparam>
public sealed class MultiplexedOutboxDispatcher<TContext> : BackgroundService
    where TContext : DbContext
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(500);
    private const int BatchSize = 50;

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<MultiplexedOutboxDispatcher<TContext>> _logger;

    public MultiplexedOutboxDispatcher(
        IServiceScopeFactory scopeFactory,
        ILogger<MultiplexedOutboxDispatcher<TContext>> logger
    )
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(PollInterval);
        while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
        {
            try
            {
                await DispatchAllTenantsOnceAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Outbox dispatcher tick failed for {DbContextType}; will retry on next tick",
                    typeof(TContext).Name
                );
            }
        }
    }

    private async Task DispatchAllTenantsOnceAsync(CancellationToken ct)
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
                    "Outbox dispatch failed for tenant {TenantSlug} ({TenantId}); other tenants continue",
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

        var factory = tenantScope.ServiceProvider.GetRequiredService<IDbContextFactory<TContext>>();
        var publisher = tenantScope.ServiceProvider.GetRequiredService<IPublishEndpoint>();

        await using var db = factory.CreateDbContext();

        var batch = await db.Set<OutboxMessage>()
            .Where(m => m.ProcessedAt == null)
            .OrderBy(m => m.CreatedAt)
            .Take(BatchSize)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        if (batch.Count == 0)
        {
            return;
        }

        foreach (var message in batch)
        {
            try
            {
                var eventType =
                    Type.GetType(message.EventType, throwOnError: false)
                    ?? throw new InvalidOperationException(
                        $"Outbox row {message.Id} references unknown event type '{message.EventType}'."
                    );

                var payload =
                    JsonSerializer.Deserialize(message.Payload, eventType)
                    ?? throw new InvalidOperationException(
                        $"Outbox row {message.Id} payload deserialised to null for type '{eventType}'."
                    );

                await publisher
                    .Publish(
                        payload,
                        eventType,
                        sendCtx =>
                        {
                            sendCtx.Headers.Set("tenant_id", tenant.Id.ToString());
                            sendCtx.Headers.Set("tenant_slug", tenant.Slug);
                            if (message.TraceId is not null)
                            {
                                sendCtx.Headers.Set("correlation_id", message.TraceId);
                            }
                        },
                        ct
                    )
                    .ConfigureAwait(false);

                message.ProcessedAt = DateTime.UtcNow;
            }
            catch (Exception ex)
            {
                message.RetryCount += 1;
                message.LastError = ex.Message;
                _logger.LogWarning(
                    ex,
                    "Outbox row {OutboxMessageId} failed to publish for tenant {TenantSlug} (retry={RetryCount})",
                    message.Id,
                    tenant.Slug,
                    message.RetryCount
                );
            }
        }

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }
}
