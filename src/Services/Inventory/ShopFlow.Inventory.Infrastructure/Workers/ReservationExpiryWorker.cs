using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ShopFlow.Inventory.Application;
using ShopFlow.Inventory.Application.Ports;
using ShopFlow.SharedKernel.Application;
using ShopFlow.SharedKernel.Application.Ports;

namespace ShopFlow.Inventory.Infrastructure.Workers;

/// <summary>
/// Multiplexed reservation-expiry worker per Tech Design v3.0 §4.5 +
/// ADR-0003. A single instance ticks on
/// <see cref="InventoryOptions.ExpiryPollIntervalSeconds"/> and, on each
/// tick, iterates every <see cref="TenantStatus.Ready"/> tenant from
/// <see cref="ITenantCatalog"/>, opens a brief per-tenant scope with
/// <see cref="RequestContext.Bind(TenantInfo, string, Guid?)"/>, and
/// runs <see cref="IReservationRepository.ReleaseExpiredAsync"/> against
/// that tenant's database.
/// </summary>
/// <remarks>
/// <para>The fan-out pattern mirrors
/// <see cref="ShopFlow.SharedKernel.Infrastructure.MultiplexedOutboxDispatcher{TContext}"/>
/// — one BackgroundService visits every tenant DB per tick, per-tenant
/// failures are caught so other tenants keep progressing, and a fresh
/// scope per tenant ensures the scoped DbContext is bound to that
/// tenant's connection string via <c>IRequestContext</c> resolution.</para>
///
/// <para>Single-instance leader election (advisory lock) for
/// multi-instance horizontal scaling is Phase-2 work; in dev / Aspire
/// the AppHost runs exactly one Inventory.Api process so this worker
/// is naturally single-leader. In production the same is true until
/// horizontal scaling lands.</para>
/// </remarks>
public sealed class ReservationExpiryWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly TimeProvider _clock;
    private readonly TimeSpan _interval;
    private readonly int _batchSize;
    private readonly ILogger<ReservationExpiryWorker> _logger;

    public ReservationExpiryWorker(
        IServiceScopeFactory scopeFactory,
        IOptions<InventoryOptions> options,
        TimeProvider clock,
        ILogger<ReservationExpiryWorker> logger
    )
    {
        ArgumentNullException.ThrowIfNull(scopeFactory);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(logger);

        var o = options.Value;
        if (o.ExpiryPollIntervalSeconds <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                o.ExpiryPollIntervalSeconds,
                "InventoryOptions.ExpiryPollIntervalSeconds must be > 0."
            );
        }
        if (o.ExpiryBatchSize <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                o.ExpiryBatchSize,
                "InventoryOptions.ExpiryBatchSize must be > 0."
            );
        }

        _scopeFactory = scopeFactory;
        _clock = clock;
        _interval = TimeSpan.FromSeconds(o.ExpiryPollIntervalSeconds);
        _batchSize = o.ExpiryBatchSize;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "ReservationExpiryWorker started; interval={IntervalSeconds}s, batchSize={Batch}",
            (int)_interval.TotalSeconds,
            _batchSize
        );

        // PeriodicTimer with TimeProvider so tests can advance the fake
        // clock instead of waiting wall time.
        using var timer = new PeriodicTimer(_interval, _clock);
        try
        {
            // Run an immediate first tick so the worker doesn't sit idle
            // for one full interval on startup — important for tests that
            // bound test duration to a few seconds.
            await TickAsync(stoppingToken).ConfigureAwait(false);
            while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
            {
                await TickAsync(stoppingToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Graceful shutdown.
        }
        finally
        {
            _logger.LogInformation("ReservationExpiryWorker stopping.");
        }
    }

    private async Task TickAsync(CancellationToken ct)
    {
        await using var rootScope = _scopeFactory.CreateAsyncScope();
        IReadOnlyList<TenantInfo> tenants;
        try
        {
            var catalog = rootScope.ServiceProvider.GetRequiredService<ITenantCatalog>();
            tenants = await catalog.GetReadyTenantsAsync(ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "ReservationExpiryWorker failed to enumerate tenants this tick.");
            return;
        }

        if (tenants.Count == 0)
        {
            return;
        }

        foreach (var tenant in tenants)
        {
            try
            {
                await ReleaseExpiredForTenantAsync(tenant, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "ReservationExpiryWorker failed for tenant {TenantSlug}; other tenants continue.",
                    tenant.Slug
                );
            }
        }
    }

    private async Task ReleaseExpiredForTenantAsync(TenantInfo tenant, CancellationToken ct)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var requestContext = scope.ServiceProvider.GetRequiredService<RequestContext>();
        requestContext.Bind(tenant, Guid.NewGuid().ToString("N"), userId: null);

        var repo = scope.ServiceProvider.GetRequiredService<IReservationRepository>();
        var nowUtc = _clock.GetUtcNow().UtcDateTime;
        var released = await repo.ReleaseExpiredAsync(nowUtc, _batchSize, ct).ConfigureAwait(false);
        if (released > 0)
        {
            _logger.LogInformation(
                "ReservationExpiryWorker released {Count} expired reservations for tenant {TenantSlug}.",
                released,
                tenant.Slug
            );
        }
    }
}
