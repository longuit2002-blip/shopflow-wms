using ShopFlow.Channel.Domain.Webhooks;
using ShopFlow.SharedKernel.Domain;

namespace ShopFlow.Channel.Application.Ports;

/// <summary>
/// Per-tenant repository for <see cref="WebhookEvent"/> rows per Sprint-4
/// plan U3. The single load-bearing call is
/// <see cref="TryInsertAsync"/> — it attempts INSERT, catches
/// <c>PostgresException 23505</c> on the <c>(channel_id, provider_event_id)</c>
/// UNIQUE constraint, and resolves to <see cref="TryInsertWebhookResult.Duplicate"/>
/// with the existing row's id. Mirrors Sprint-1-redux's
/// <c>ReservationRepository.TryReserveLinesAsync</c>.
/// </summary>
public interface IWebhookEventRepository
{
    /// <summary>
    /// Idempotent insert. First write wins; replay returns the existing row.
    /// Does not call <c>SaveChangesAsync</c> — the orchestrator commits via
    /// <see cref="IUnitOfWork"/> alongside the outbox row.
    /// </summary>
    Task<Result<TryInsertWebhookResult>> TryInsertAsync(
        WebhookEvent webhookEvent,
        CancellationToken ct
    );

    /// <summary>
    /// Read by id — used by integration tests + the operator queue surface
    /// (Phase-3 Sprint-7).
    /// </summary>
    Task<WebhookEvent?> FindByIdAsync(Guid id, CancellationToken ct);
}

/// <summary>
/// Discriminated outcome of <see cref="IWebhookEventRepository.TryInsertAsync"/>.
/// </summary>
public sealed record TryInsertWebhookResult(Guid EventId, bool IsDuplicate);
