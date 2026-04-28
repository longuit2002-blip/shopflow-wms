using System.Text.Json;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace ShopFlow.SharedKernel.Infrastructure;

/// <summary>
/// Mode A (polling) outbox dispatcher per Tech Design §11.3. A
/// <see cref="BackgroundService"/> wakes every 500 ms, pulls a batch of up
/// to 50 unprocessed rows from <see cref="OutboxMessage"/>, publishes each
/// via <see cref="IPublishEndpoint"/>, and stamps <c>ProcessedAt</c> on
/// success.
///
/// <para>
/// Mode B (LISTEN/NOTIFY) and Mode C (Debezium CDC) are post-MVP upgrades;
/// they do not change the row shape. Polling is correct, simple, and ships
/// before W6 — it just has a ~500 ms p99 latency floor that small-SaaS
/// scale tolerates.
/// </para>
/// </summary>
/// <typeparam name="TDbContext">
/// The module's DbContext type. The dispatcher resolves a fresh scope per
/// tick to avoid leaking the change tracker across iterations.
/// </typeparam>
public sealed class OutboxDispatcher<TDbContext> : BackgroundService
    where TDbContext : DbContext
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(500);
    private const int BatchSize = 50;

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<OutboxDispatcher<TDbContext>> _logger;

    public OutboxDispatcher(
        IServiceScopeFactory scopeFactory,
        ILogger<OutboxDispatcher<TDbContext>> logger
    )
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // PeriodicTimer is the .NET 8-native answer; it doesn't drift on
        // long ticks and integrates cleanly with cancellation.
        using var timer = new PeriodicTimer(PollInterval);
        while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
        {
            try
            {
                await DispatchOnceAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Outbox dispatcher iteration failed; will retry on next tick");
            }
        }
    }

    private async Task DispatchOnceAsync(CancellationToken cancellationToken)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TDbContext>();
        var publisher = scope.ServiceProvider.GetRequiredService<IPublishEndpoint>();

        var batch = await db.Set<OutboxMessage>()
            .Where(m => m.ProcessedAt == null)
            .OrderBy(m => m.CreatedAt)
            .Take(BatchSize)
            .ToListAsync(cancellationToken)
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

#pragma warning disable ShopFlow0002 // outbox dispatcher restores context from the OutboxMessage row, not IRequestContext
                await publisher
                    .Publish(payload, eventType, cancellationToken)
                    .ConfigureAwait(false);
#pragma warning restore ShopFlow0002

                message.ProcessedAt = DateTime.UtcNow;
            }
            catch (Exception ex)
            {
                message.RetryCount += 1;
                message.LastError = ex.Message;
                _logger.LogWarning(
                    ex,
                    "Outbox row {OutboxMessageId} failed to publish (retry={RetryCount})",
                    message.Id,
                    message.RetryCount
                );
            }
        }

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }
}
