using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Polly.CircuitBreaker;
using ShopFlow.Channel.Application.Adapters;
using ShopFlow.SharedKernel.Application;
using ShopFlow.SharedKernel.Application.Ports;
using ShopFlow.SharedKernel.Domain;
using ShopFlow.StockSync.Application.Dispatch;
using ShopFlow.StockSync.Application.Ports;
using ShopFlow.StockSync.Domain.Aggregates;
using ShopFlow.StockSync.Infrastructure.Breaker;
using ShopFlow.StockSync.Infrastructure.RateLimit;

namespace ShopFlow.StockSync.Infrastructure.Dispatch;

/// <summary>
/// Sprint-5 plan U5 — per-tenant push dispatcher. On startup the
/// service enumerates every <see cref="TenantStatus.Ready"/> tenant
/// from <see cref="ITenantCatalog"/> and launches one long-running
/// <see cref="Task"/> per tenant that drains the tenant's
/// <see cref="IPerTenantQueue"/>, runs each
/// <see cref="PushIntent"/> through the
/// per-<c>(tenant, channel)</c> token bucket
/// (<see cref="TenantChannelBucketRegistry"/>) and circuit breaker
/// (<see cref="TenantChannelBreakerRegistry"/>), invokes the channel
/// adapter, and writes one audit row to
/// <c>stock_sync_push_log</c> via <see cref="IPushLogRepository"/>.
/// </summary>
/// <remarks>
/// <para>Sprint-5 limitation: tenants are enumerated exactly once on
/// startup. Tenant-added events trigger re-enumeration in Phase-3 —
/// for the portfolio scope and the noisy-neighbor scale gate (5
/// fixed tenants) the static set is sufficient.</para>
///
/// <para>Sprint-5 limitation: the
/// <see cref="StockUpdateRequest.ChannelId"/> we hand to the adapter
/// is <see cref="Guid.Empty"/> — Sprint-5 routes by channel <em>type</em>
/// only via <see cref="IChannelAdapterFactory.ResolveFor"/>. Wiring a
/// real per-tenant channel id from the Channel module's
/// <c>channels</c> table is Phase-3 work; the Shopee mock endpoint
/// (U6) ignores ChannelId for the same reason.</para>
///
/// <para>Each per-tenant <see cref="DispatchLoopAsync"/> is wrapped in
/// a try/catch that swallows non-cancellation exceptions and keeps the
/// loop running — one corrupt intent must not take down the whole
/// tenant's pipeline. Cancellation propagates so graceful shutdown
/// works.</para>
///
/// <para>Scope handling mirrors the Sprint-1-redux
/// <c>ReservationExpiryWorker</c>: one
/// <see cref="IServiceScopeFactory.CreateAsyncScope"/> per processed
/// intent, with <c>RequestContext.Bind</c> populating the per-request
/// tenant. The DbContext factory injected into
/// <see cref="IPushLogRepository"/> resolves to that tenant's DB.</para>
/// </remarks>
public sealed class PerTenantDispatcherService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IPerTenantQueue _queue;
    private readonly TenantChannelBucketRegistry _bucketRegistry;
    private readonly TenantChannelBreakerRegistry _breakerRegistry;
    private readonly IChannelAdapterFactory _adapterFactory;
    private readonly TimeProvider _clock;
    private readonly ILogger<PerTenantDispatcherService> _logger;

    public PerTenantDispatcherService(
        IServiceScopeFactory scopeFactory,
        IPerTenantQueue queue,
        TenantChannelBucketRegistry bucketRegistry,
        TenantChannelBreakerRegistry breakerRegistry,
        IChannelAdapterFactory adapterFactory,
        TimeProvider clock,
        ILogger<PerTenantDispatcherService> logger
    )
    {
        ArgumentNullException.ThrowIfNull(scopeFactory);
        ArgumentNullException.ThrowIfNull(queue);
        ArgumentNullException.ThrowIfNull(bucketRegistry);
        ArgumentNullException.ThrowIfNull(breakerRegistry);
        ArgumentNullException.ThrowIfNull(adapterFactory);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(logger);

        _scopeFactory = scopeFactory;
        _queue = queue;
        _bucketRegistry = bucketRegistry;
        _breakerRegistry = breakerRegistry;
        _adapterFactory = adapterFactory;
        _clock = clock;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        IReadOnlyList<TenantInfo> tenants;
        try
        {
            await using var rootScope = _scopeFactory.CreateAsyncScope();
            var catalog = rootScope.ServiceProvider.GetRequiredService<ITenantCatalog>();
            tenants = await catalog
                .GetReadyTenantsAsync(stoppingToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            return;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "PerTenantDispatcherService failed to enumerate tenants on startup; no dispatch loops will run."
            );
            return;
        }

        if (tenants.Count == 0)
        {
            _logger.LogInformation(
                "PerTenantDispatcherService started; no ready tenants — dispatcher idle."
            );
            return;
        }

        _logger.LogInformation(
            "PerTenantDispatcherService started; provisioning {Count} dispatch loops.",
            tenants.Count
        );

        // Long-running per-tenant task — Task.Run gets each loop its
        // own thread-pool root and prevents one slow tenant from
        // starving the next on a single scheduler queue. Phase-3 will
        // re-enumerate tenants on tenant-added events.
        var loops = tenants
            .Select(t => Task.Run(
                () => DispatchLoopAsync(t, stoppingToken),
                stoppingToken
            ))
            .ToArray();

        await Task.WhenAll(loops).ConfigureAwait(false);
    }

    private async Task DispatchLoopAsync(TenantInfo tenant, CancellationToken ct)
    {
        _logger.LogInformation(
            "Dispatch loop online for tenant {TenantSlug} ({TenantId}).",
            tenant.Slug,
            tenant.Id
        );

        while (!ct.IsCancellationRequested)
        {
            PushIntent intent;
            try
            {
                intent = await _queue.ReadNextAsync(tenant.Id, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Dispatch loop for tenant {TenantSlug} failed to read next intent; continuing.",
                    tenant.Slug
                );
                continue;
            }

            try
            {
                await ProcessIntentAsync(tenant, intent, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Dispatch loop for tenant {TenantSlug} swallowed unexpected exception for SKU {Sku} channel {ChannelType}; loop continues.",
                    tenant.Slug,
                    intent.Sku,
                    intent.ChannelType
                );
            }
        }

        _logger.LogInformation(
            "Dispatch loop draining for tenant {TenantSlug} ({TenantId}).",
            tenant.Slug,
            tenant.Id
        );
    }

    private async Task ProcessIntentAsync(
        TenantInfo tenant,
        PushIntent intent,
        CancellationToken ct
    )
    {
        // Up-front breaker gate — if the breaker is Open we don't
        // even try to acquire a token (don't waste rate-limit budget
        // on a doomed call). Plan U5 mandates one push_log row with
        // status BreakerOpen so audit shows every observed intent.
        var breakerState = _breakerRegistry.GetState(intent.TenantId, intent.ChannelType);
        if (string.Equals(breakerState, "Open", StringComparison.Ordinal))
        {
            await AppendLogAsync(
                tenant,
                PushLogEntry.MarkBreakerOpen(
                    intent.TenantId,
                    intent.ChannelType,
                    intent.Sku,
                    intent.Available,
                    intent.IdempotencyKey,
                    intent.ObservedAt,
                    _clock.GetUtcNow().UtcDateTime
                ),
                ct
            ).ConfigureAwait(false);
            return;
        }

        // Token bucket — Sprint-5 ships per-tenant per-channel
        // rate limiting. Overflow (queue limit exceeded) returns
        // IsAcquired=false; we drop the intent and continue. A
        // Phase-3 enhancement could re-enqueue or surface a metric.
        using var lease = await _bucketRegistry
            .AcquireAsync(intent.TenantId, intent.ChannelType, ct)
            .ConfigureAwait(false);
        if (!lease.IsAcquired)
        {
            _logger.LogDebug(
                "Token bucket overflow for tenant {TenantSlug} channel {ChannelType}; intent for SKU {Sku} dropped.",
                tenant.Slug,
                intent.ChannelType,
                intent.Sku
            );
            return;
        }

        var pipeline = _breakerRegistry.GetOrCreate(intent.TenantId, intent.ChannelType);
        var stopwatch = Stopwatch.StartNew();
        Result pushResult;
        try
        {
            pushResult = await pipeline
                .ExecuteAsync(
                    async (state, token) =>
                    {
                        var adapter = state.factory.TryResolve(state.intent.ChannelType);
                        if (adapter is null)
                        {
                            return Result.Failure(
                                $"Unknown channel type '{state.intent.ChannelType}'.",
                                "stocksync.adapter.unknown_channel"
                            );
                        }

                        // Sprint-5 routes by channel TYPE only — ChannelId
                        // wiring per tenant is Phase-3. The Shopee mock
                        // endpoint (U6) ignores ChannelId so Guid.Empty
                        // is safe for the portfolio scope.
                        var request = new StockUpdateRequest(
                            ChannelId: Guid.Empty,
                            ExternalSku: state.intent.Sku,
                            Quantity: state.intent.Available,
                            ObservedAt: state.intent.ObservedAt,
                            IdempotencyKey: state.intent.IdempotencyKey
                        );
                        return await adapter
                            .PushStockUpdateAsync(request, token)
                            .ConfigureAwait(false);
                    },
                    (intent, factory: _adapterFactory),
                    ct
                )
                .ConfigureAwait(false);
        }
        catch (BrokenCircuitException)
        {
            // Race: breaker tripped between our up-front check and the
            // execute call. Log as a Failed row with the breaker code
            // — the audit row still distinguishes pre-check rejection
            // (status BreakerOpen) from mid-execute trip
            // (status Failed + error_code stocksync.breaker.open).
            pushResult = Result.Failure(
                "Circuit breaker tripped between state check and execute.",
                "stocksync.breaker.open"
            );
        }
        finally
        {
            stopwatch.Stop();
        }

        PushLogEntry entry = pushResult.IsSuccess
            ? PushLogEntry.MarkSucceeded(
                intent.TenantId,
                intent.ChannelType,
                intent.Sku,
                intent.Available,
                intent.IdempotencyKey,
                latencyMs: (int)stopwatch.ElapsedMilliseconds,
                observedAt: intent.ObservedAt,
                pushedAt: _clock.GetUtcNow().UtcDateTime
            )
            : PushLogEntry.MarkFailed(
                intent.TenantId,
                intent.ChannelType,
                intent.Sku,
                intent.Available,
                intent.IdempotencyKey,
                errorCode: pushResult.ErrorCode ?? "stocksync.push.unknown",
                latencyMs: (int)stopwatch.ElapsedMilliseconds,
                observedAt: intent.ObservedAt,
                pushedAt: _clock.GetUtcNow().UtcDateTime
            );

        await AppendLogAsync(tenant, entry, ct).ConfigureAwait(false);
    }

    private async Task AppendLogAsync(
        TenantInfo tenant,
        PushLogEntry entry,
        CancellationToken ct
    )
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var requestContext = scope.ServiceProvider.GetRequiredService<RequestContext>();
        requestContext.Bind(tenant, Guid.NewGuid().ToString("N"), userId: null);

        var repo = scope.ServiceProvider.GetRequiredService<IPushLogRepository>();
        await repo.AppendAsync(entry, ct).ConfigureAwait(false);
    }
}
