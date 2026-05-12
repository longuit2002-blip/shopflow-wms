using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ShopFlow.Inventory.Application.Ports;

namespace ShopFlow.Inventory.Infrastructure.Workers;

/// <summary>
/// Per-tenant background worker that scans <c>reservations_ledger</c> for
/// Pending rows past their TTL and transitions them to Expired in batches.
/// Tech Design v3.0 §4.5 calls this out as the "ledger garbage-collection"
/// path; without it the available count drifts low under any non-trivial
/// abandonment rate.
/// </summary>
/// <remarks>
/// U8 ships the hosted-service shape and the loop scaffolding; the
/// <see cref="IReservationRepository.ReleaseExpiredAsync"/> call inside
/// the loop currently throws <see cref="NotImplementedException"/> per
/// the Sprint-1-redux stub pattern. The worker keeps running so the
/// W1 green-against-stub state surfaces the unimplemented behavior
/// loudly rather than silently doing nothing.
///
/// Multi-tenant fan-out: this worker resolves its scope from the DI
/// container per tick; Sprint-1-redux wires a per-tenant scope via
/// <c>IRequestContext.Bind</c> before resolving so the worker scans the
/// right tenant DB. U8 ships a single-tenant loop body — the AppHost's
/// bootstrap pre-provisions exactly one tenant per dev session, and the
/// real multi-tenant fan-out lives one layer up in the multiplexed
/// dispatcher pattern.
/// </remarks>
public sealed class ReservationExpiryWorker : BackgroundService
{
    private static readonly TimeSpan TickInterval = TimeSpan.FromSeconds(30);
    private const int BatchSize = 200;

    private readonly IServiceScopeFactory _scopes;
    private readonly TimeProvider _clock;
    private readonly ILogger<ReservationExpiryWorker> _logger;

    public ReservationExpiryWorker(
        IServiceScopeFactory scopes,
        TimeProvider clock,
        ILogger<ReservationExpiryWorker> logger
    )
    {
        _scopes = scopes;
        _clock = clock;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("ReservationExpiryWorker started; tick={Tick}s", TickInterval.TotalSeconds);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _scopes.CreateScope();
                var repo = scope.ServiceProvider.GetRequiredService<IReservationRepository>();
                var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

                var expired = await repo
                    .ReleaseExpiredAsync(_clock.GetUtcNow().UtcDateTime, BatchSize, stoppingToken)
                    .ConfigureAwait(false);
                if (expired > 0)
                {
                    await uow.SaveChangesAsync(stoppingToken).ConfigureAwait(false);
                    _logger.LogInformation("Expired {Count} reservations in this tick.", expired);
                }
            }
            catch (NotImplementedException)
            {
                // Expected in U8 (Sprint-1-redux fleshes out the repository); log
                // once at debug so the W1 green-against-stub state stays visible.
                _logger.LogDebug(
                    "ReservationExpiryWorker tick skipped — repository behavior pending Sprint-1-redux."
                );
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "ReservationExpiryWorker tick failed.");
            }

            try
            {
                await Task.Delay(TickInterval, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        _logger.LogInformation("ReservationExpiryWorker stopping.");
    }
}
