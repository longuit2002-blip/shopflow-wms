using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging;
using Npgsql;
using ShopFlow.Outbound.Domain;

namespace ShopFlow.Outbound.Infrastructure.Persistence;

/// <summary>
/// Sprint-7.5 U8 — idempotent guard for the
/// <c>outbound_saga_transitions</c> table. Closes Sprint-7 trade-off #1
/// (double-audit-write under MT redelivery).
/// </summary>
/// <remarks>
/// <para><strong>Why an interceptor.</strong> Per KTD6 the 23505 surfaces
/// at the saga's <c>SaveChangesAsync</c> boundary — NOT at
/// <see cref="ShopFlow.Outbound.Application.Ports.IOrderTransitionRepository.AppendAsync"/>
/// time, which only stages the entity via <c>AddAsync</c> without flushing.
/// The MT EF saga repository owns the actual SaveChanges call site, so
/// wrapping <c>AppendAsync</c> would catch nothing; the unhandled
/// <c>DbUpdateException</c> would propagate, MT would roll back the saga
/// state row, and the redelivery would loop indefinitely (Sprint-1-redux
/// <c>ReservationRepository</c>, Sprint-4 <c>WebhookEventRepository</c>,
/// and Sprint-5 <c>SkuFlagRepository</c> precedents all execute immediate
/// INSERTs so their 23505 surfaces synchronously — the saga path is
/// structurally different).</para>
///
/// <para><strong>How idempotency works.</strong> Two-layer:</para>
/// <list type="number">
///   <item><description><see cref="SavingChangesAsync"/> is the primary
///     mechanism. It scans the <c>ChangeTracker</c> for
///     <see cref="OrderTransition"/> entries in
///     <see cref="EntityState.Added"/>, issues a single batched <c>EXISTS</c>
///     probe against the target table per row, and detaches any entity
///     whose <c>(OrderId, OccurredAt, ToState)</c> triple already exists.
///     The save then proceeds without conflict; the saga state row +
///     outbox row commit normally, the audit row is silently skipped.</description></item>
///   <item><description><see cref="SaveChangesFailedAsync"/> is defensive.
///     Under concurrent inserts of the same triple (rare race between
///     two consume scopes) the pre-check can miss; if Postgres then raises
///     the UNIQUE violation, the failure callback logs at Debug. The
///     exception still propagates (we cannot suppress it from this
///     callback) — MT will redeliver, and on the second consume the
///     pre-check now catches the duplicate cleanly.</description></item>
/// </list>
///
/// <para><strong>Specificity.</strong> The interceptor swallows ONLY the
/// <c>23505</c> SqlState against the constraint name
/// <c>uq_outbound_saga_transitions_order_occurred_state</c>. Any other
/// <c>DbUpdateException</c> (e.g. <c>23502</c> not-null, <c>23503</c>
/// foreign-key, or 23505 against a different constraint) propagates
/// unchanged so genuine bugs surface loudly.</para>
/// </remarks>
public sealed class SagaTransitionDuplicateInterceptor : SaveChangesInterceptor
{
    /// <summary>
    /// UNIQUE constraint name shared with migration
    /// <c>20260519000002_AddUniqueOnSagaTransitions</c> and
    /// <c>OrderTransitionConfiguration</c>. Hard-coded so the
    /// <see cref="SaveChangesFailedAsync"/> branch can match against the
    /// exact name Postgres reports.
    /// </summary>
    public const string UniqueConstraintName = "uq_outbound_saga_transitions_order_occurred_state";

    private readonly ILogger<SagaTransitionDuplicateInterceptor> _logger;

    public SagaTransitionDuplicateInterceptor(ILogger<SagaTransitionDuplicateInterceptor> logger)
    {
        _logger = logger;
    }

    public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(eventData);

        var context = eventData.Context;
        if (context is null)
        {
            return await base.SavingChangesAsync(eventData, result, cancellationToken)
                .ConfigureAwait(false);
        }

        // Pre-check: for every OrderTransition being added, probe the DB
        // to see if the (OrderId, OccurredAt, ToState) triple already
        // exists. If so, detach the entity so SaveChanges does not attempt
        // the duplicate INSERT.
        var addedTransitions = context
            .ChangeTracker.Entries<OrderTransition>()
            .Where(e => e.State == EntityState.Added)
            .ToList();

        if (addedTransitions.Count == 0)
        {
            return await base.SavingChangesAsync(eventData, result, cancellationToken)
                .ConfigureAwait(false);
        }

        var dbSet = context.Set<OrderTransition>();

        foreach (var entry in addedTransitions)
        {
            var transition = entry.Entity;
            var exists = await dbSet
                .AsNoTracking()
                .AnyAsync(
                    t =>
                        t.OrderId == transition.OrderId
                        && t.OccurredAt == transition.OccurredAt
                        && t.ToState == transition.ToState,
                    cancellationToken
                )
                .ConfigureAwait(false);

            if (exists)
            {
                _logger.LogDebug(
                    "SagaTransitionDuplicateInterceptor: detaching duplicate OrderTransition for order {OrderId} at {OccurredAt} → {ToState} (pre-check matched existing row)",
                    transition.OrderId,
                    transition.OccurredAt,
                    transition.ToState
                );
                entry.State = EntityState.Detached;
            }
        }

        return await base.SavingChangesAsync(eventData, result, cancellationToken)
            .ConfigureAwait(false);
    }

    public override Task SaveChangesFailedAsync(
        DbContextErrorEventData eventData,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(eventData);

        if (IsDuplicateSagaTransitionViolation(eventData.Exception))
        {
            // Defensive log only — the exception still propagates so MT
            // redelivers the message. On the next consume the pre-check in
            // SavingChangesAsync detaches the duplicate cleanly and the
            // commit succeeds.
            _logger.LogDebug(
                eventData.Exception,
                "SagaTransitionDuplicateInterceptor: Postgres 23505 on {ConstraintName} reached SaveChangesFailedAsync; relying on MT redelivery + next-consume pre-check for idempotency",
                UniqueConstraintName
            );
        }

        return base.SaveChangesFailedAsync(eventData, cancellationToken);
    }

    /// <summary>
    /// Returns <c>true</c> iff the exception is a <see cref="DbUpdateException"/>
    /// whose inner exception is a <see cref="PostgresException"/> with
    /// <see cref="PostgresException.SqlState"/> == <c>23505</c> AND
    /// <see cref="PostgresException.ConstraintName"/> ==
    /// <see cref="UniqueConstraintName"/>. Any other shape (different
    /// SqlState, different constraint, non-Postgres exception) returns
    /// <c>false</c> so genuine errors propagate. Public so unit tests can
    /// drive the branching without spinning a real Postgres.
    /// </summary>
    public static bool IsDuplicateSagaTransitionViolation(Exception? exception)
    {
        if (exception is not DbUpdateException dbEx)
        {
            return false;
        }

        if (dbEx.InnerException is not PostgresException pg)
        {
            return false;
        }

        return Classify(pg.SqlState, pg.ConstraintName);
    }

    /// <summary>
    /// Pure classifier broken out so unit tests can exercise the
    /// SqlState + ConstraintName branching without constructing a real
    /// <see cref="PostgresException"/> (whose <c>ConstraintName</c> is
    /// read-only in Npgsql 9.x — only populated by the wire protocol).
    /// </summary>
    public static bool Classify(string? sqlState, string? constraintName)
    {
        return sqlState == PostgresErrorCodes.UniqueViolation
            && string.Equals(constraintName, UniqueConstraintName, StringComparison.Ordinal);
    }
}
