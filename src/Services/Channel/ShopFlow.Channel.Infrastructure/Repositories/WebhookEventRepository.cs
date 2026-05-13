using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using ShopFlow.Channel.Application.Ports;
using ShopFlow.Channel.Domain.Webhooks;
using ShopFlow.SharedKernel.Domain;

namespace ShopFlow.Channel.Infrastructure.Repositories;

/// <summary>
/// EF Core + Npgsql implementation of <see cref="IWebhookEventRepository"/>
/// per Sprint-4 plan U3. The load-bearing detail is the
/// <c>PostgresErrorCodes.UniqueViolation</c> catch on the
/// <c>(channel_id, provider_event_id)</c> UNIQUE constraint — duplicates
/// roll back, SELECT the existing row, and return
/// <see cref="TryInsertWebhookResult.IsDuplicate"/> = true. Pattern mirrors
/// Sprint-1-redux's <c>ReservationRepository.TryReserveLinesAsync</c>.
/// </summary>
public sealed class WebhookEventRepository : IWebhookEventRepository
{
    private readonly ChannelDbContext _db;

    public WebhookEventRepository(ChannelDbContext db)
    {
        _db = db;
    }

    public async Task<Result<TryInsertWebhookResult>> TryInsertAsync(
        WebhookEvent webhookEvent,
        CancellationToken ct
    )
    {
        ArgumentNullException.ThrowIfNull(webhookEvent);

        IDbContextTransaction? transaction = null;
        try
        {
            transaction = await _db
                .Database.BeginTransactionAsync(
                    System.Data.IsolationLevel.ReadCommitted,
                    ct
                )
                .ConfigureAwait(false);

            await _db.WebhookEvents.AddAsync(webhookEvent, ct).ConfigureAwait(false);

            try
            {
                await _db.SaveChangesAsync(ct).ConfigureAwait(false);
            }
            catch (DbUpdateException ex)
                when (ex.InnerException is PostgresException pg
                    && pg.SqlState == PostgresErrorCodes.UniqueViolation)
            {
                // Replay: roll back the failed insert and resolve the existing
                // row id by composite UNIQUE key. The orchestrator interprets
                // IsDuplicate=true as "do NOT append outbox row" per R3.
                await transaction.RollbackAsync(ct).ConfigureAwait(false);

                // Detach the rejected pending entity so future SaveChanges
                // calls in the same scope don't re-attempt the insert.
                _db.Entry(webhookEvent).State = EntityState.Detached;

                var existingId = await _db
                    .WebhookEvents.AsNoTracking()
                    .Where(e =>
                        e.ChannelId == webhookEvent.ChannelId
                        && e.ProviderEventId == webhookEvent.ProviderEventId
                    )
                    .Select(e => e.Id)
                    .FirstOrDefaultAsync(ct)
                    .ConfigureAwait(false);

                if (existingId == Guid.Empty)
                {
                    return Result<TryInsertWebhookResult>.Failure(
                        "idempotency conflict but no existing webhook row found.",
                        "webhook.idempotency_conflict_no_row"
                    );
                }

                return Result<TryInsertWebhookResult>.Success(
                    new TryInsertWebhookResult(existingId, IsDuplicate: true)
                );
            }

            await transaction.CommitAsync(ct).ConfigureAwait(false);
            return Result<TryInsertWebhookResult>.Success(
                new TryInsertWebhookResult(webhookEvent.Id, IsDuplicate: false)
            );
        }
        finally
        {
            if (transaction is not null)
            {
                await transaction.DisposeAsync().ConfigureAwait(false);
            }
        }
    }

    public Task<WebhookEvent?> FindByIdAsync(Guid id, CancellationToken ct) =>
        _db.WebhookEvents.AsNoTracking().FirstOrDefaultAsync(e => e.Id == id, ct);
}
