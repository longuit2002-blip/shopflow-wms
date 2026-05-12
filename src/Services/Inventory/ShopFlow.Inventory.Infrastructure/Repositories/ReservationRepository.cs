using System.Data;
using System.Diagnostics;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using NpgsqlTypes;
using ShopFlow.Inventory.Application.Ports;
using ShopFlow.Inventory.Domain;
using ShopFlow.Inventory.Domain.Events;
using ShopFlow.SharedKernel.Application;
using ShopFlow.SharedKernel.Domain;
using ShopFlow.SharedKernel.Infrastructure;

namespace ShopFlow.Inventory.Infrastructure.Repositories;

/// <summary>
/// EF Core + raw-SQL implementation of <see cref="IReservationRepository"/>
/// per Tech Design v3.0 §4.4. The hot path <see cref="TryReserveAsync"/>
/// uses a single-round-trip conditional-CTE that UPDATEs
/// <c>stock_items</c> (the row lock under READ COMMITTED) and INSERTs
/// the ledger row in the same statement, plus an EF-tracked
/// <see cref="OutboxMessage"/> for the domain event — all under one
/// transaction.
/// </summary>
/// <remarks>
/// <para><strong>Isolation: READ COMMITTED, not SERIALIZABLE.</strong> The
/// load-bearing correctness primitive is the <c>UPDATE … WHERE available &gt;= @qty
/// RETURNING sku</c> inside the CTE — Postgres serialises concurrent
/// UPDATEs on the same row via row locks, so two concurrent reserves
/// against the same SKU cannot both succeed beyond the available
/// count. SERIALIZABLE adds 40001 retry overhead with no correctness
/// benefit (per Tech Design v3.0 §4.4 + ADR-0003). The plan's risk row
/// notes that if a future load test surfaces an actual race, the
/// remediation is <c>SELECT … FOR UPDATE</c> inside the CTE — not a
/// regression to SERIALIZABLE.</para>
///
/// <para><strong>Idempotency, layered.</strong> Application-level
/// short-circuit via <see cref="FindByOrderIdAsync"/> handles the common
/// retry. Database-level <c>UNIQUE(order_id)</c> handles the concurrent-
/// same-order race; the <c>23505</c> exception is caught, the transaction
/// rolled back, and the existing row returned to the caller. Both layers
/// resolve to the same outcome: one ledger row per <c>order_id</c>, the
/// caller sees Success.</para>
///
/// <para><strong>Outbox path.</strong> The interceptor harvests events
/// from tracked <see cref="BaseEntity"/> entities, but the hot path
/// never loads the aggregate via EF — the ledger row is created via raw
/// SQL. So each public method that needs to emit an event adds an
/// <see cref="OutboxMessage"/> directly via the DbSet, stamping
/// <c>TenantId</c> from <see cref="IRequestContext"/>. The
/// <c>OutboxInterceptor</c> still fires but finds no tracked aggregates
/// with events; the manually-added outbox row flushes in the same
/// <see cref="DbContext.SaveChangesAsync(CancellationToken)"/> call as
/// part of the same Postgres transaction we opened.</para>
/// </remarks>
public sealed class ReservationRepository : IReservationRepository
{
    private static readonly JsonSerializerOptions OutboxJsonOptions = new()
    {
        WriteIndented = false,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly InventoryDbContext _db;
    private readonly TimeProvider _clock;
    private readonly IRequestContext _requestContext;

    public ReservationRepository(
        InventoryDbContext db,
        TimeProvider clock,
        IRequestContext requestContext
    )
    {
        _db = db;
        _clock = clock;
        _requestContext = requestContext;
    }

    public async Task<Result<Reservation>> TryReserveAsync(
        Sku sku,
        string orderId,
        Quantity quantity,
        TimeSpan ttl,
        CancellationToken ct
    )
    {
        ArgumentNullException.ThrowIfNull(sku);
        ArgumentNullException.ThrowIfNull(quantity);
        if (string.IsNullOrWhiteSpace(orderId))
        {
            return Result<Reservation>.Failure(
                "order_id is required.",
                "reservation.order_id_required"
            );
        }
        if (quantity.Value == 0)
        {
            return Result<Reservation>.Failure(
                "quantity must be > 0.",
                "reservation.quantity_zero"
            );
        }
        if (ttl <= TimeSpan.Zero)
        {
            return Result<Reservation>.Failure(
                "ttl must be > 0.",
                "reservation.ttl_non_positive"
            );
        }

        // Application-level idempotency short-circuit (the common retry path).
        var existing = await FindByOrderIdAsync(orderId, ct).ConfigureAwait(false);
        if (existing is not null)
        {
            return Result<Reservation>.Success(existing);
        }

        var nowUtc = _clock.GetUtcNow().UtcDateTime;
        var expiresAt = nowUtc + ttl;
        var reservationId = Guid.NewGuid();

        await using var transaction = await _db
            .Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct)
            .ConfigureAwait(false);

        var connection = (NpgsqlConnection)_db.Database.GetDbConnection();
        Guid? insertedId;

        try
        {
            await using (var cmd = connection.CreateCommand())
            {
                cmd.CommandText = """
                    WITH upd AS (
                        UPDATE stock_items
                           SET available  = available - @p_qty,
                               reserved   = reserved + @p_qty,
                               updated_at = @p_now
                         WHERE sku = @p_sku
                           AND available >= @p_qty
                        RETURNING sku
                    )
                    INSERT INTO reservations_ledger
                        (id, sku, order_id, quantity, status, expires_at, created_at, updated_at)
                    SELECT @p_id, @p_sku, @p_order, @p_qty, 'Pending', @p_expires, @p_now, NULL
                      FROM upd
                    RETURNING id;
                    """;
                cmd.Transaction = (NpgsqlTransaction)transaction.GetDbTransaction();
                cmd.Parameters.Add(
                    new NpgsqlParameter("p_sku", NpgsqlDbType.Varchar) { Value = sku.Value }
                );
                cmd.Parameters.Add(
                    new NpgsqlParameter("p_qty", NpgsqlDbType.Integer) { Value = quantity.Value }
                );
                cmd.Parameters.Add(
                    new NpgsqlParameter("p_id", NpgsqlDbType.Uuid) { Value = reservationId }
                );
                cmd.Parameters.Add(
                    new NpgsqlParameter("p_order", NpgsqlDbType.Varchar) { Value = orderId }
                );
                cmd.Parameters.Add(
                    new NpgsqlParameter("p_now", NpgsqlDbType.TimestampTz) { Value = nowUtc }
                );
                cmd.Parameters.Add(
                    new NpgsqlParameter("p_expires", NpgsqlDbType.TimestampTz)
                    {
                        Value = expiresAt,
                    }
                );

                try
                {
                    var scalar = await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false);
                    insertedId = scalar is null or DBNull ? null : (Guid)scalar;
                }
                catch (PostgresException ex)
                    when (ex.SqlState == PostgresErrorCodes.UniqueViolation)
                {
                    await transaction.RollbackAsync(ct).ConfigureAwait(false);
                    var race = await FindByOrderIdAsync(orderId, ct).ConfigureAwait(false);
                    return race is not null
                        ? Result<Reservation>.Success(race)
                        : Result<Reservation>.Failure(
                            "idempotency conflict but no existing reservation found.",
                            "reservation.idempotency_conflict"
                        );
                }
            }

            if (insertedId is null)
            {
                await transaction.RollbackAsync(ct).ConfigureAwait(false);
                return Result<Reservation>.Failure("oversold.", "reservation.insufficient_stock");
            }

            AppendOutbox(
                new StockReservedEvent(
                    insertedId.Value,
                    sku.Value,
                    orderId,
                    quantity.Value,
                    nowUtc
                ),
                nowUtc
            );
            await _db.SaveChangesAsync(ct).ConfigureAwait(false);
            await transaction.CommitAsync(ct).ConfigureAwait(false);
        }
        catch
        {
            try
            {
                await transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
            }
            catch
            {
                // Best-effort rollback — propagate the original exception.
            }
            throw;
        }

        var inserted =
            await FindByOrderIdAsync(orderId, ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException(
                "TryReserveAsync inserted a reservation row but FindByOrderIdAsync could not read it back — invariant violation."
            );
        return Result<Reservation>.Success(inserted);
    }

    public Task<Reservation?> FindByOrderIdAsync(string orderId, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(orderId);
        return _db.Reservations.AsNoTracking().FirstOrDefaultAsync(r => r.OrderId == orderId, ct);
    }

    public async Task<Result> ConfirmAsync(string orderId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(orderId))
        {
            return Result.Failure("order_id is required.", "reservation.order_id_required");
        }

        var nowUtc = _clock.GetUtcNow().UtcDateTime;

        await using var transaction = await _db
            .Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct)
            .ConfigureAwait(false);
        var connection = (NpgsqlConnection)_db.Database.GetDbConnection();

        string? sku = null;
        int qty = 0;
        int availableAfter = 0;
        int reservedAfter = 0;

        try
        {
            await using (var cmd = connection.CreateCommand())
            {
                cmd.CommandText = """
                    WITH r AS (
                        UPDATE reservations_ledger
                           SET status       = 'Confirmed',
                               confirmed_at = @p_now,
                               updated_at   = @p_now
                         WHERE order_id = @p_order
                           AND status   = 'Pending'
                        RETURNING sku, quantity
                    ),
                    s AS (
                        UPDATE stock_items si
                           SET reserved   = si.reserved - r.quantity,
                               updated_at = @p_now
                          FROM r
                         WHERE si.sku = r.sku
                        RETURNING si.sku, si.available, si.reserved
                    )
                    SELECT s.sku, r.quantity, s.available, s.reserved FROM s, r;
                    """;
                cmd.Transaction = (NpgsqlTransaction)transaction.GetDbTransaction();
                cmd.Parameters.Add(
                    new NpgsqlParameter("p_order", NpgsqlDbType.Varchar) { Value = orderId }
                );
                cmd.Parameters.Add(
                    new NpgsqlParameter("p_now", NpgsqlDbType.TimestampTz) { Value = nowUtc }
                );

                await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
                if (await reader.ReadAsync(ct).ConfigureAwait(false))
                {
                    sku = reader.GetString(0);
                    qty = reader.GetInt32(1);
                    availableAfter = reader.GetInt32(2);
                    reservedAfter = reader.GetInt32(3);
                }
            }

            if (sku is null)
            {
                await transaction.RollbackAsync(ct).ConfigureAwait(false);
                return await ClassifyMissingPendingAsync(orderId, "confirm", ct)
                    .ConfigureAwait(false);
            }

            AppendOutbox(new StockChangedEvent(sku, availableAfter, reservedAfter, nowUtc), nowUtc);
            await _db.SaveChangesAsync(ct).ConfigureAwait(false);
            await transaction.CommitAsync(ct).ConfigureAwait(false);
            _ = qty;
            return Result.Success();
        }
        catch
        {
            try
            {
                await transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
            }
            catch
            {
                // Best-effort rollback.
            }
            throw;
        }
    }

    public async Task<Result> ReleaseAsync(string orderId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(orderId))
        {
            return Result.Failure("order_id is required.", "reservation.order_id_required");
        }

        var nowUtc = _clock.GetUtcNow().UtcDateTime;

        await using var transaction = await _db
            .Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct)
            .ConfigureAwait(false);
        var connection = (NpgsqlConnection)_db.Database.GetDbConnection();

        Guid? reservationId = null;
        string? sku = null;
        int qty = 0;
        int availableAfter = 0;
        int reservedAfter = 0;

        try
        {
            await using (var cmd = connection.CreateCommand())
            {
                cmd.CommandText = """
                    WITH r AS (
                        UPDATE reservations_ledger
                           SET status      = 'Released',
                               released_at = @p_now,
                               updated_at  = @p_now
                         WHERE order_id = @p_order
                           AND status   = 'Pending'
                        RETURNING id, sku, quantity
                    ),
                    s AS (
                        UPDATE stock_items si
                           SET reserved   = si.reserved - r.quantity,
                               available  = si.available + r.quantity,
                               updated_at = @p_now
                          FROM r
                         WHERE si.sku = r.sku
                        RETURNING si.sku, si.available, si.reserved
                    )
                    SELECT r.id, s.sku, r.quantity, s.available, s.reserved FROM s, r;
                    """;
                cmd.Transaction = (NpgsqlTransaction)transaction.GetDbTransaction();
                cmd.Parameters.Add(
                    new NpgsqlParameter("p_order", NpgsqlDbType.Varchar) { Value = orderId }
                );
                cmd.Parameters.Add(
                    new NpgsqlParameter("p_now", NpgsqlDbType.TimestampTz) { Value = nowUtc }
                );

                await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
                if (await reader.ReadAsync(ct).ConfigureAwait(false))
                {
                    reservationId = reader.GetGuid(0);
                    sku = reader.GetString(1);
                    qty = reader.GetInt32(2);
                    availableAfter = reader.GetInt32(3);
                    reservedAfter = reader.GetInt32(4);
                }
            }

            if (sku is null)
            {
                await transaction.RollbackAsync(ct).ConfigureAwait(false);
                return await ClassifyMissingPendingAsync(orderId, "release", ct)
                    .ConfigureAwait(false);
            }

            AppendOutbox(
                new StockReleasedEvent(
                    reservationId!.Value,
                    sku,
                    orderId,
                    qty,
                    StockReleaseReason.Cancelled,
                    nowUtc
                ),
                nowUtc
            );
            AppendOutbox(new StockChangedEvent(sku, availableAfter, reservedAfter, nowUtc), nowUtc);
            await _db.SaveChangesAsync(ct).ConfigureAwait(false);
            await transaction.CommitAsync(ct).ConfigureAwait(false);
            return Result.Success();
        }
        catch
        {
            try
            {
                await transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
            }
            catch
            {
                // Best-effort rollback.
            }
            throw;
        }
    }

    public async Task<int> ReleaseExpiredAsync(DateTime now, int batchSize, CancellationToken ct)
    {
        if (batchSize <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(batchSize),
                batchSize,
                "batch_size must be > 0."
            );
        }

        await using var transaction = await _db
            .Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct)
            .ConfigureAwait(false);
        var connection = (NpgsqlConnection)_db.Database.GetDbConnection();

        var expired = new List<(Guid Id, string Sku, string OrderId, int Quantity)>();

        try
        {
            await using (var cmd = connection.CreateCommand())
            {
                // SKIP LOCKED so concurrent expiry workers (Phase-2 multi-instance)
                // never wait on each other; one batch per worker per tick.
                cmd.CommandText = """
                    WITH candidates AS (
                        SELECT id
                          FROM reservations_ledger
                         WHERE status     = 'Pending'
                           AND expires_at < @p_now
                         ORDER BY expires_at
                         LIMIT @p_batch
                         FOR UPDATE SKIP LOCKED
                    ),
                    expired AS (
                        UPDATE reservations_ledger r
                           SET status     = 'Expired',
                               expired_at = @p_now,
                               updated_at = @p_now
                          FROM candidates c
                         WHERE r.id = c.id
                        RETURNING r.id, r.sku, r.order_id, r.quantity
                    ),
                    agg AS (
                        SELECT sku, SUM(quantity)::int AS released_qty FROM expired GROUP BY sku
                    ),
                    stock_update AS (
                        UPDATE stock_items si
                           SET reserved   = si.reserved - a.released_qty,
                               available  = si.available + a.released_qty,
                               updated_at = @p_now
                          FROM agg a
                         WHERE si.sku = a.sku
                        RETURNING si.sku
                    )
                    SELECT id, sku, order_id, quantity FROM expired;
                    """;
                cmd.Transaction = (NpgsqlTransaction)transaction.GetDbTransaction();
                cmd.Parameters.Add(
                    new NpgsqlParameter("p_now", NpgsqlDbType.TimestampTz) { Value = now }
                );
                cmd.Parameters.Add(
                    new NpgsqlParameter("p_batch", NpgsqlDbType.Integer) { Value = batchSize }
                );

                await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
                while (await reader.ReadAsync(ct).ConfigureAwait(false))
                {
                    expired.Add(
                        (
                            reader.GetGuid(0),
                            reader.GetString(1),
                            reader.GetString(2),
                            reader.GetInt32(3)
                        )
                    );
                }
            }

            if (expired.Count == 0)
            {
                await transaction.RollbackAsync(ct).ConfigureAwait(false);
                return 0;
            }

            var occurredAt = _clock.GetUtcNow().UtcDateTime;
            foreach (var row in expired)
            {
                AppendOutbox(
                    new StockReleasedEvent(
                        row.Id,
                        row.Sku,
                        row.OrderId,
                        row.Quantity,
                        StockReleaseReason.Expired,
                        occurredAt
                    ),
                    occurredAt
                );
            }
            await _db.SaveChangesAsync(ct).ConfigureAwait(false);
            await transaction.CommitAsync(ct).ConfigureAwait(false);
            return expired.Count;
        }
        catch
        {
            try
            {
                await transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
            }
            catch
            {
                // Best-effort rollback.
            }
            throw;
        }
    }

    private async Task<Result> ClassifyMissingPendingAsync(
        string orderId,
        string operation,
        CancellationToken ct
    )
    {
        var current = await FindByOrderIdAsync(orderId, ct).ConfigureAwait(false);
        if (current is null)
        {
            return Result.Failure(
                $"reservation not found for order '{orderId}'.",
                "reservation.not_found"
            );
        }
        return (operation, current.Status) switch
        {
            ("confirm", ReservationStatus.Confirmed) => Result.Failure(
                "already confirmed.",
                "reservation.already_confirmed"
            ),
            ("release", ReservationStatus.Released) => Result.Failure(
                "already released.",
                "reservation.already_released"
            ),
            _ => Result.Failure(
                $"reservation for order '{orderId}' is in {current.Status} state; '{operation}' requires Pending.",
                "reservation.invalid_state"
            ),
        };
    }

    private void AppendOutbox(SharedKernel.Domain.IDomainEvent ev, DateTime occurredAt)
    {
        var traceId = Activity.Current?.TraceId.ToString();
        _db.OutboxMessages.Add(
            new OutboxMessage
            {
                Id = Guid.NewGuid(),
                TenantId = _requestContext.TenantId,
                EventType = ev.GetType().AssemblyQualifiedName!,
                Payload = JsonSerializer.Serialize(ev, ev.GetType(), OutboxJsonOptions),
                TraceId = traceId,
                CreatedAt = occurredAt,
            }
        );
    }
}
