using System.Data;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using NpgsqlTypes;
using ShopFlow.Contracts.Inventory;
using ShopFlow.Inventory.Application;
using ShopFlow.Inventory.Application.Ports;
using ShopFlow.Inventory.Domain;
using ShopFlow.Inventory.Domain.Events;
using ShopFlow.SharedKernel.Application;
using ShopFlow.SharedKernel.Domain;
using ShopFlow.SharedKernel.Infrastructure;

namespace ShopFlow.Inventory.Infrastructure.Repositories;

/// <summary>
/// EF Core + raw-SQL implementation of <see cref="IReservationRepository"/>
/// per Tech Design v3.0 §4.4 plus the Sprint-3-redux multi-line extension
/// (K10/K11). The hot path <see cref="TryReserveLinesAsync"/> uses a
/// single-round-trip multi-row CTE that UPDATEs <c>stock_items</c> (the
/// row lock under READ COMMITTED) and INSERTs N ledger rows in the same
/// statement, plus an EF-tracked <see cref="OutboxMessage"/> for the
/// domain event — all under one transaction. The single-line
/// <see cref="TryReserveAsync"/> wrapper forwards to the multi-line method
/// with <c>order_line_id='_default'</c> so pre-Sprint-3 callers are unchanged.
/// </summary>
/// <remarks>
/// <para><strong>Isolation: READ COMMITTED, not SERIALIZABLE.</strong> The
/// load-bearing correctness primitive is the <c>UPDATE … WHERE available &gt;= @qty
/// RETURNING sku</c> inside the CTE — Postgres serialises concurrent
/// UPDATEs on the same row via row locks, so two concurrent reserves
/// against the same SKU cannot both succeed beyond the available
/// count. SERIALIZABLE adds 40001 retry overhead with no correctness
/// benefit (per Tech Design v3.0 §4.4 + ADR-0003). For the multi-line
/// path Sprint-3-redux K11 adds a <c>will_succeed</c> aggregate inside
/// the CTE so the per-SKU UPDATE only runs when EVERY requested line has
/// sufficient stock — atomic all-or-nothing semantics on top of READ
/// COMMITTED's per-row guarantee. Same-SKU multi-line cases are handled
/// by aggregating the desired quantity per sku before the availability
/// check.</para>
///
/// <para><strong>Idempotency, layered.</strong> Application-level
/// short-circuit via <see cref="FindByOrderIdAsync"/> handles the common
/// retry for the single-line path. Database-level composite UNIQUE
/// <c>(order_id, order_line_id)</c> handles the concurrent-same-order race
/// for both single-line and multi-line paths; the <c>23505</c> exception
/// is caught, the transaction rolled back, and the existing rows
/// returned to the caller. Both layers resolve to the same outcome: one
/// ledger row per <c>(order_id, order_line_id)</c>, the caller sees
/// Success.</para>
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

        // Sprint-3-redux U3 wrapper: route through the multi-line path with
        // a single-element list using the default order_line_id. External
        // behavior (success row shape, oversold code, idempotency outcome)
        // unchanged from Sprint-1-redux.
        var lines = new[]
        {
            new LineReservation(sku, Reservation.DefaultOrderLineId, quantity),
        };
        var multi = await TryReserveLinesAsync(orderId, lines, ttl, ct).ConfigureAwait(false);
        if (multi.IsSuccess)
        {
            return Result<Reservation>.Success(multi.Reservations[0]);
        }
        return Result<Reservation>.Failure(
            multi.Error ?? "oversold.",
            // Map the multi-line code to the single-line public code so
            // pre-Sprint-3 callers (ReservationRepositoryTests +
            // PropertyTests) keep seeing "reservation.insufficient_stock".
            multi.ErrorCode == "reservation.oversold"
                ? "reservation.insufficient_stock"
                : multi.ErrorCode
        );
    }

    public async Task<TryReserveLinesResult> TryReserveLinesAsync(
        string orderId,
        IReadOnlyList<LineReservation> lines,
        TimeSpan ttl,
        CancellationToken ct
    )
    {
        if (string.IsNullOrWhiteSpace(orderId))
        {
            return TryReserveLinesResult.Failure(
                "order_id is required.",
                "reservation.order_id_required",
                Array.Empty<LineOutcome>()
            );
        }
        ArgumentNullException.ThrowIfNull(lines);
        if (lines.Count == 0)
        {
            return TryReserveLinesResult.Failure(
                "at least one line is required.",
                "reservation.no_lines",
                Array.Empty<LineOutcome>()
            );
        }
        foreach (var l in lines)
        {
            ArgumentNullException.ThrowIfNull(l);
            ArgumentNullException.ThrowIfNull(l.Sku);
            ArgumentNullException.ThrowIfNull(l.Quantity);
            if (l.Quantity.Value == 0)
            {
                return TryReserveLinesResult.Failure(
                    "quantity must be > 0.",
                    "reservation.quantity_zero",
                    Array.Empty<LineOutcome>()
                );
            }
            if (string.IsNullOrWhiteSpace(l.OrderLineId))
            {
                return TryReserveLinesResult.Failure(
                    "order_line_id is required.",
                    "reservation.order_line_id_required",
                    Array.Empty<LineOutcome>()
                );
            }
        }
        if (ttl <= TimeSpan.Zero)
        {
            return TryReserveLinesResult.Failure(
                "ttl must be > 0.",
                "reservation.ttl_non_positive",
                Array.Empty<LineOutcome>()
            );
        }

        var nowUtc = _clock.GetUtcNow().UtcDateTime;
        var expiresAt = nowUtc + ttl;

        await using var transaction = await _db
            .Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct)
            .ConfigureAwait(false);
        var connection = (NpgsqlConnection)_db.Database.GetDbConnection();

        var insertedRows = new List<(Guid Id, string Sku, string OrderLineId, int Qty)>();
        try
        {
            await using (var cmd = connection.CreateCommand())
            {
                cmd.Transaction = (NpgsqlTransaction)transaction.GetDbTransaction();
                BuildTryReserveLinesCommand(cmd, orderId, lines, nowUtc, expiresAt);

                try
                {
                    await using var reader = await cmd
                        .ExecuteReaderAsync(ct)
                        .ConfigureAwait(false);
                    while (await reader.ReadAsync(ct).ConfigureAwait(false))
                    {
                        insertedRows.Add(
                            (
                                reader.GetGuid(0),
                                reader.GetString(1),
                                reader.GetString(2),
                                reader.GetInt32(3)
                            )
                        );
                    }
                }
                catch (PostgresException ex)
                    when (ex.SqlState == PostgresErrorCodes.UniqueViolation)
                {
                    await transaction.RollbackAsync(ct).ConfigureAwait(false);
                    var existing = await ReadExistingRowsAsync(orderId, ct).ConfigureAwait(false);
                    if (existing.Count == 0)
                    {
                        return TryReserveLinesResult.Failure(
                            "idempotency conflict but no existing reservation rows found.",
                            "reservation.idempotency_conflict",
                            Array.Empty<LineOutcome>()
                        );
                    }
                    var outcomes = existing
                        .Select(r => new LineOutcome(
                            r.OrderLineId,
                            r.Sku,
                            r.Id,
                            LineOutcomeStatus.Reserved
                        ))
                        .ToList();
                    return TryReserveLinesResult.Success(existing, outcomes);
                }
            }

            // Zero rows inserted ⇒ atomic oversell. Roll back first so the
            // outcome computation reads the actual committed availability
            // (the failed transaction may have partially UPDATEd some skus
            // before the all_succeeded gate killed the INSERT; the rollback
            // unwinds those before we report per-line outcomes).
            if (insertedRows.Count == 0)
            {
                await transaction.RollbackAsync(ct).ConfigureAwait(false);
                var outcomes = await ComputeOversoldOutcomesAsync(lines, ct).ConfigureAwait(false);
                return TryReserveLinesResult.Failure(
                    "oversold.",
                    "reservation.oversold",
                    outcomes
                );
            }

            // Hydrate Reservation entities from the inserted rows + emit one
            // StockReservedEvent outbox row per line. Ordering of returned
            // reservations follows the input `lines` order so the caller can
            // align indexes.
            var insertedById = insertedRows.ToDictionary(r => r.OrderLineId, r => r);
            var reservations = new List<Reservation>(insertedRows.Count);
            var outcomesSuccess = new List<LineOutcome>(insertedRows.Count);
            foreach (var line in lines)
            {
                if (!insertedById.TryGetValue(line.OrderLineId, out var row))
                {
                    // Defensive: every requested line should have an inserted
                    // row at this point (will_succeed = true ⇒ all rows
                    // inserted). If something diverged, fail loud.
                    throw new InvalidOperationException(
                        $"TryReserveLinesAsync: inserted-row map missing line '{line.OrderLineId}' — invariant violation."
                    );
                }

                var reservation = MaterializeReservation(
                    row.Id,
                    line,
                    orderId,
                    expiresAt
                );
                reservations.Add(reservation);
                outcomesSuccess.Add(
                    new LineOutcome(
                        line.OrderLineId,
                        line.Sku,
                        row.Id,
                        LineOutcomeStatus.Reserved
                    )
                );

                AppendOutbox(
                    new StockReservedEvent(
                        row.Id,
                        line.Sku.Value,
                        orderId,
                        line.Quantity.Value,
                        nowUtc
                    ),
                    nowUtc
                );
            }

            // Sprint-5 U2 / KTD1 — emit one StockLevelChangedV1 per unique
            // affected SKU using post-commit `available`. The CTE updates
            // stock_items but does not surface the new available; the helper
            // does a follow-up SELECT inside the same transaction.
            var affectedSkus = insertedRows.Select(r => r.Sku).Distinct().ToArray();
            var perSkuAvailable = await ReadAvailableForSkusAsync(
                connection,
                transaction,
                affectedSkus,
                ct
            ).ConfigureAwait(false);
            foreach (var (sku, available) in perSkuAvailable)
            {
                AppendOutbox(
                    new StockLevelChangedV1(_requestContext.TenantId, sku, available, nowUtc),
                    nowUtc
                );
            }

            await _db.SaveChangesAsync(ct).ConfigureAwait(false);
            await transaction.CommitAsync(ct).ConfigureAwait(false);

            return TryReserveLinesResult.Success(reservations, outcomesSuccess);
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
    }

    /// <summary>
    /// Build the atomic-multi-line CTE command. Key correctness property:
    /// the availability check (<c>si.available &gt;= dps.total_qty</c>) is
    /// embedded INSIDE the UPDATE's WHERE clause — this is what Sprint-1-redux's
    /// single-line CTE does, and it's what serialises concurrent writes
    /// against the same stock_items row under READ COMMITTED. Postgres
    /// acquires the row lock on UPDATE, re-reads the row under the
    /// post-lock committed snapshot, then evaluates the WHERE predicate.
    /// Two concurrent reserves against the same SKU cannot both succeed
    /// beyond the available count because the second waiter sees the
    /// decremented value after the first commits. A pre-UPDATE "will_succeed"
    /// CTE (the plan's original pseudocode) is unsafe here — it evaluates
    /// availability under the transaction's own snapshot before acquiring
    /// any row locks, so concurrent transactions can both pass the gate
    /// and both UPDATE blindly. The plan pseudocode is corrected here per
    /// the K11 "predicate-in-UPDATE" Postgres pattern; reported as a
    /// deviation in the U3 deliverable.
    /// </summary>
    /// <remarks>
    /// Atomicity across N lines is enforced via the <c>all_succeeded</c>
    /// gate: the INSERT runs only when every distinct sku in the
    /// <c>desired_per_sku</c> set produced a deducted row. If any sku
    /// fails the predicate, the deducted set is smaller than the
    /// desired_per_sku set ⇒ INSERT skipped ⇒ caller sees zero
    /// inserted rows ⇒ explicit rollback unwinds the partial UPDATE.
    /// Other transactions waiting on the locked rows resume against the
    /// rolled-back values.
    /// </remarks>
    private static void BuildTryReserveLinesCommand(
        NpgsqlCommand cmd,
        string orderId,
        IReadOnlyList<LineReservation> lines,
        DateTime nowUtc,
        DateTime expiresAt
    )
    {
        var sb = new StringBuilder();
        sb.Append(
            """
            WITH desired(sku, order_line_id, qty, reservation_id) AS (VALUES
            """
        );
        for (var i = 0; i < lines.Count; i++)
        {
            if (i > 0)
            {
                sb.Append(", ");
            }
            sb.Append('(');
            sb.Append("@p_sku_").Append(i).Append("::varchar, ");
            sb.Append("@p_line_").Append(i).Append("::text, ");
            sb.Append("@p_qty_").Append(i).Append("::int, ");
            sb.Append("@p_resid_").Append(i).Append("::uuid");
            sb.Append(')');
        }
        sb.Append(
            """
            ),
            -- Aggregate desired qty per sku for the availability check;
            -- supports same-sku-multi-line cases (two lines drawing from
            -- one stock_items row sum their qty before the predicate).
            desired_per_sku AS (
                SELECT sku, SUM(qty)::int AS total_qty FROM desired GROUP BY sku
            ),
            -- The conditional UPDATE: per-row availability predicate inside
            -- the WHERE clause is what serialises concurrent writes under
            -- READ COMMITTED. Rows that fail the predicate are not
            -- returned; the deducted set may be a strict subset of
            -- desired_per_sku.
            deducted AS (
                UPDATE stock_items si
                   SET available  = si.available - dps.total_qty,
                       reserved   = si.reserved + dps.total_qty,
                       updated_at = @p_now
                  FROM desired_per_sku dps
                 WHERE si.sku = dps.sku
                   AND si.available >= dps.total_qty
                RETURNING si.sku
            ),
            -- All-or-nothing gate: the INSERT runs only when every distinct
            -- desired sku produced a deducted row. Implemented via a
            -- NOT EXISTS check against any desired sku that's missing from
            -- the deducted set.
            all_succeeded AS (
                SELECT 1 AS ok
                 WHERE NOT EXISTS (
                    SELECT 1 FROM desired_per_sku dps
                     WHERE NOT EXISTS (SELECT 1 FROM deducted d WHERE d.sku = dps.sku)
                 )
            ),
            inserted AS (
                INSERT INTO reservations_ledger
                    (id, sku, order_id, order_line_id, quantity, status, expires_at, created_at)
                SELECT d.reservation_id, d.sku, @p_order, d.order_line_id, d.qty,
                       'Pending', @p_expires, @p_now
                  FROM desired d
                 WHERE EXISTS (SELECT 1 FROM all_succeeded)
                RETURNING id, sku, order_line_id, quantity
            )
            SELECT id, sku, order_line_id, quantity FROM inserted;
            """
        );
        cmd.CommandText = sb.ToString();

        for (var i = 0; i < lines.Count; i++)
        {
            cmd.Parameters.Add(
                new NpgsqlParameter($"p_sku_{i}", NpgsqlDbType.Varchar)
                {
                    Value = lines[i].Sku.Value,
                }
            );
            cmd.Parameters.Add(
                new NpgsqlParameter($"p_line_{i}", NpgsqlDbType.Text)
                {
                    Value = lines[i].OrderLineId,
                }
            );
            cmd.Parameters.Add(
                new NpgsqlParameter($"p_qty_{i}", NpgsqlDbType.Integer)
                {
                    Value = lines[i].Quantity.Value,
                }
            );
            cmd.Parameters.Add(
                new NpgsqlParameter($"p_resid_{i}", NpgsqlDbType.Uuid)
                {
                    Value = Guid.NewGuid(),
                }
            );
        }
        cmd.Parameters.Add(
            new NpgsqlParameter("p_order", NpgsqlDbType.Varchar) { Value = orderId }
        );
        cmd.Parameters.Add(
            new NpgsqlParameter("p_now", NpgsqlDbType.TimestampTz) { Value = nowUtc }
        );
        cmd.Parameters.Add(
            new NpgsqlParameter("p_expires", NpgsqlDbType.TimestampTz) { Value = expiresAt }
        );
    }

    /// <summary>
    /// Hydrate a <see cref="Reservation"/> from raw row data without
    /// running <see cref="Reservation.Create"/>'s validation (the row
    /// already exists, validation already passed at insert time). Mirrors
    /// the EF materialisation path so the returned aggregate matches what
    /// a subsequent <see cref="FindByOrderIdAsync"/> would surface.
    /// </summary>
    private static Reservation MaterializeReservation(
        Guid id,
        LineReservation line,
        string orderId,
        DateTime expiresAt
    )
    {
        // Reservation.Create requires a TTL > Zero; we have an absolute
        // expiresAt here, so reconstruct a TTL that's at least one tick > 0.
        // We don't read the inserted row back via FindByOrderIdAsync because
        // for multi-line it returns just one of N; cheaper to hydrate here.
        var ttl = expiresAt - DateTime.UtcNow;
        if (ttl <= TimeSpan.Zero)
        {
            ttl = TimeSpan.FromTicks(1);
        }
        var build = Reservation.Create(
            line.Sku,
            orderId,
            line.Quantity,
            ttl,
            now: DateTime.UtcNow,
            orderLineId: line.OrderLineId
        );
        var reservation = build.Value!;
        // Force the Id to match the DB-inserted row id. The Create path
        // assigns a fresh Guid (BaseEntity ctor); we patch via reflection
        // because the Id setter is private. Used only for the in-memory
        // shape; not load-bearing for correctness (the canonical row is
        // in the DB).
        var idProp = typeof(BaseEntity).GetProperty(
            nameof(BaseEntity.Id),
            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance
        );
        idProp!.SetValue(reservation, id);
        return reservation;
    }

    /// <summary>
    /// Compute per-line outcomes for an atomic-oversell failure: read
    /// current <c>stock_items.available</c> per requested sku (aggregated)
    /// and mark each line PASS or OVERSOLD relative to the line's own
    /// quantity. For same-sku-multi-line failures we report each line
    /// individually against the per-sku total; the first line that pushes
    /// the running total past availability is OVERSOLD, earlier ones in
    /// the input order are PASS. The saga uses these outcomes only as
    /// diagnostic detail — the canonical atomic-failure semantic is "no
    /// rows were inserted".
    /// </summary>
    /// <remarks>
    /// Opens a fresh Npgsql connection rather than reusing the EF
    /// DbContext's connection. The caller has just rolled back the
    /// transaction, which closes the EF connection (Npgsql's transaction
    /// disposal behavior). The fresh connection sees the
    /// post-rollback committed state — exactly what we want.
    /// </remarks>
    private async Task<IReadOnlyList<LineOutcome>> ComputeOversoldOutcomesAsync(
        IReadOnlyList<LineReservation> lines,
        CancellationToken ct
    )
    {
        var distinctSkus = lines.Select(l => l.Sku.Value).Distinct().ToArray();
        var availability = new Dictionary<string, int>();

        await using var fresh = new NpgsqlConnection(_db.Database.GetConnectionString());
        await fresh.OpenAsync(ct).ConfigureAwait(false);
        await using (var cmd = fresh.CreateCommand())
        {
            cmd.CommandText =
                "SELECT sku, available FROM stock_items WHERE sku = ANY(@p_skus)";
            cmd.Parameters.Add(
                new NpgsqlParameter("p_skus", NpgsqlDbType.Array | NpgsqlDbType.Varchar)
                {
                    Value = distinctSkus,
                }
            );
            await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
            while (await reader.ReadAsync(ct).ConfigureAwait(false))
            {
                availability[reader.GetString(0)] = reader.GetInt32(1);
            }
        }

        var remaining = new Dictionary<string, int>();
        foreach (var sku in distinctSkus)
        {
            remaining[sku] = availability.TryGetValue(sku, out var v) ? v : 0;
        }

        var outcomes = new List<LineOutcome>(lines.Count);
        foreach (var line in lines)
        {
            var has = remaining[line.Sku.Value];
            var status =
                has >= line.Quantity.Value
                    ? LineOutcomeStatus.Reserved
                    : LineOutcomeStatus.Oversold;
            if (status == LineOutcomeStatus.Reserved)
            {
                remaining[line.Sku.Value] = has - line.Quantity.Value;
            }
            outcomes.Add(new LineOutcome(line.OrderLineId, line.Sku, null, status));
        }
        return outcomes;
    }

    private async Task<List<Reservation>> ReadExistingRowsAsync(
        string orderId,
        CancellationToken ct
    )
    {
        var rows = await _db
            .Reservations.AsNoTracking()
            .Where(r => r.OrderId == orderId)
            .OrderBy(r => r.OrderLineId)
            .ToListAsync(ct)
            .ConfigureAwait(false);
        return rows;
    }

    public Task<Reservation?> FindByOrderIdAsync(string orderId, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(orderId);
        return _db.Reservations.AsNoTracking().FirstOrDefaultAsync(r => r.OrderId == orderId, ct);
    }

    /// <summary>
    /// Confirm a Pending order — flips ALL rows for <paramref name="orderId"/>
    /// to Confirmed. Sprint-3-redux: for multi-line orders this is N rows
    /// not 1, but the WHERE clause already matches all of them. The per-sku
    /// stock_items update aggregates quantities so multi-line orders with
    /// shared SKUs decrement the reserved count correctly.
    /// </summary>
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

        var perSku = new List<(string Sku, int AvailableAfter, int ReservedAfter, int Qty)>();

        try
        {
            await using (var cmd = connection.CreateCommand())
            {
                // Per-sku aggregation handles multi-line orders that share a
                // sku (UPDATE FROM r without aggregation would only see one
                // row per sku and the decrement would be wrong for N lines
                // on one SKU). Result set is one row per distinct sku.
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
                    agg AS (
                        SELECT sku, SUM(quantity)::int AS total_qty FROM r GROUP BY sku
                    ),
                    s AS (
                        UPDATE stock_items si
                           SET reserved   = si.reserved - a.total_qty,
                               updated_at = @p_now
                          FROM agg a
                         WHERE si.sku = a.sku
                        RETURNING si.sku, si.available, si.reserved
                    )
                    SELECT s.sku, s.available, s.reserved, a.total_qty FROM s, agg a WHERE s.sku = a.sku;
                    """;
                cmd.Transaction = (NpgsqlTransaction)transaction.GetDbTransaction();
                cmd.Parameters.Add(
                    new NpgsqlParameter("p_order", NpgsqlDbType.Varchar) { Value = orderId }
                );
                cmd.Parameters.Add(
                    new NpgsqlParameter("p_now", NpgsqlDbType.TimestampTz) { Value = nowUtc }
                );

                await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
                while (await reader.ReadAsync(ct).ConfigureAwait(false))
                {
                    perSku.Add(
                        (
                            reader.GetString(0),
                            reader.GetInt32(1),
                            reader.GetInt32(2),
                            reader.GetInt32(3)
                        )
                    );
                }
            }

            if (perSku.Count == 0)
            {
                await transaction.RollbackAsync(ct).ConfigureAwait(false);
                return await ClassifyMissingPendingAsync(orderId, "confirm", ct)
                    .ConfigureAwait(false);
            }

            foreach (var (sku, available, reserved, _) in perSku)
            {
                AppendOutbox(new StockChangedEvent(sku, available, reserved, nowUtc), nowUtc);
                // Sprint-5 U2 / KTD1 — cross-module integration event.
                AppendOutbox(
                    new StockLevelChangedV1(_requestContext.TenantId, sku, available, nowUtc),
                    nowUtc
                );
            }
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

    /// <summary>
    /// Full-order release — flips ALL Pending rows for <paramref name="orderId"/>
    /// to Released. Sprint-3-redux: for multi-line orders this is N rows.
    /// </summary>
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

        var released = new List<(Guid Id, string Sku, int Quantity, string OrderLineId)>();
        var perSkuAfter = new List<(string Sku, int Available, int Reserved)>();

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
                        RETURNING id, sku, quantity, order_line_id
                    ),
                    agg AS (
                        SELECT sku, SUM(quantity)::int AS total_qty FROM r GROUP BY sku
                    ),
                    s AS (
                        UPDATE stock_items si
                           SET reserved   = si.reserved - a.total_qty,
                               available  = si.available + a.total_qty,
                               updated_at = @p_now
                          FROM agg a
                         WHERE si.sku = a.sku
                        RETURNING si.sku, si.available, si.reserved
                    )
                    SELECT r.id, r.sku, r.quantity, r.order_line_id, s.available, s.reserved
                      FROM r JOIN s ON s.sku = r.sku;
                    """;
                cmd.Transaction = (NpgsqlTransaction)transaction.GetDbTransaction();
                cmd.Parameters.Add(
                    new NpgsqlParameter("p_order", NpgsqlDbType.Varchar) { Value = orderId }
                );
                cmd.Parameters.Add(
                    new NpgsqlParameter("p_now", NpgsqlDbType.TimestampTz) { Value = nowUtc }
                );

                await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
                while (await reader.ReadAsync(ct).ConfigureAwait(false))
                {
                    released.Add(
                        (
                            reader.GetGuid(0),
                            reader.GetString(1),
                            reader.GetInt32(2),
                            reader.GetString(3)
                        )
                    );
                    perSkuAfter.Add((reader.GetString(1), reader.GetInt32(4), reader.GetInt32(5)));
                }
            }

            if (released.Count == 0)
            {
                await transaction.RollbackAsync(ct).ConfigureAwait(false);
                return await ClassifyMissingPendingAsync(orderId, "release", ct)
                    .ConfigureAwait(false);
            }

            foreach (var row in released)
            {
                AppendOutbox(
                    new StockReleasedEvent(
                        row.Id,
                        row.Sku,
                        orderId,
                        row.Quantity,
                        StockReleaseReason.Cancelled,
                        nowUtc
                    ),
                    nowUtc
                );
            }
            // Distinct per-sku snapshot for the stock_changed event.
            var distinctSkus = perSkuAfter.GroupBy(p => p.Sku).Select(g => g.Last());
            foreach (var (sku, available, reserved) in distinctSkus)
            {
                AppendOutbox(new StockChangedEvent(sku, available, reserved, nowUtc), nowUtc);
                // Sprint-5 U2 / KTD1 — cross-module integration event.
                AppendOutbox(
                    new StockLevelChangedV1(_requestContext.TenantId, sku, available, nowUtc),
                    nowUtc
                );
            }
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

    public async Task<ReleaseLinesResult> ReleaseLinesAsync(
        string orderId,
        IReadOnlyList<string> orderLineIds,
        CancellationToken ct
    )
    {
        ArgumentNullException.ThrowIfNull(orderLineIds);
        if (string.IsNullOrWhiteSpace(orderId))
        {
            throw new ArgumentException(
                "order_id is required.",
                nameof(orderId)
            );
        }
        if (orderLineIds.Count == 0)
        {
            // Empty input set — nothing to release. Caller should route to
            // ReleaseAsync(orderId) for the full-release case; this path is
            // an explicit "release nothing" no-op.
            return new ReleaseLinesResult(Array.Empty<string>());
        }

        var nowUtc = _clock.GetUtcNow().UtcDateTime;

        await using var transaction = await _db
            .Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct)
            .ConfigureAwait(false);
        var connection = (NpgsqlConnection)_db.Database.GetDbConnection();

        var released = new List<(Guid Id, string Sku, int Quantity, string OrderLineId)>();
        var perSkuAfter = new List<(string Sku, int Available, int Reserved)>();

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
                           AND order_line_id = ANY(@p_lines)
                           AND status   = 'Pending'
                        RETURNING id, sku, quantity, order_line_id
                    ),
                    agg AS (
                        SELECT sku, SUM(quantity)::int AS total_qty FROM r GROUP BY sku
                    ),
                    s AS (
                        UPDATE stock_items si
                           SET reserved   = si.reserved - a.total_qty,
                               available  = si.available + a.total_qty,
                               updated_at = @p_now
                          FROM agg a
                         WHERE si.sku = a.sku
                        RETURNING si.sku, si.available, si.reserved
                    )
                    SELECT r.id, r.sku, r.quantity, r.order_line_id, s.available, s.reserved
                      FROM r JOIN s ON s.sku = r.sku;
                    """;
                cmd.Transaction = (NpgsqlTransaction)transaction.GetDbTransaction();
                cmd.Parameters.Add(
                    new NpgsqlParameter("p_order", NpgsqlDbType.Varchar) { Value = orderId }
                );
                cmd.Parameters.Add(
                    new NpgsqlParameter("p_lines", NpgsqlDbType.Array | NpgsqlDbType.Text)
                    {
                        Value = orderLineIds.ToArray(),
                    }
                );
                cmd.Parameters.Add(
                    new NpgsqlParameter("p_now", NpgsqlDbType.TimestampTz) { Value = nowUtc }
                );

                await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
                while (await reader.ReadAsync(ct).ConfigureAwait(false))
                {
                    released.Add(
                        (
                            reader.GetGuid(0),
                            reader.GetString(1),
                            reader.GetInt32(2),
                            reader.GetString(3)
                        )
                    );
                    perSkuAfter.Add((reader.GetString(1), reader.GetInt32(4), reader.GetInt32(5)));
                }
            }

            if (released.Count == 0)
            {
                // Idempotent no-op — nothing matched (either all already
                // released, or the line ids never existed for this order).
                // Treat as success with an empty list per the K11 contract.
                await transaction.RollbackAsync(ct).ConfigureAwait(false);
                return new ReleaseLinesResult(Array.Empty<string>());
            }

            foreach (var row in released)
            {
                AppendOutbox(
                    new StockReleasedEvent(
                        row.Id,
                        row.Sku,
                        orderId,
                        row.Quantity,
                        StockReleaseReason.Cancelled,
                        nowUtc
                    ),
                    nowUtc
                );
            }
            var distinctSkus = perSkuAfter.GroupBy(p => p.Sku).Select(g => g.Last());
            foreach (var (sku, available, reserved) in distinctSkus)
            {
                AppendOutbox(new StockChangedEvent(sku, available, reserved, nowUtc), nowUtc);
                // Sprint-5 U2 / KTD1 — cross-module integration event.
                AppendOutbox(
                    new StockLevelChangedV1(_requestContext.TenantId, sku, available, nowUtc),
                    nowUtc
                );
            }

            await _db.SaveChangesAsync(ct).ConfigureAwait(false);
            await transaction.CommitAsync(ct).ConfigureAwait(false);
            return new ReleaseLinesResult(released.Select(r => r.OrderLineId).ToArray());
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

            // Sprint-5 U2 / KTD1 — emit one StockLevelChangedV1 per unique
            // affected SKU. The expiry CTE updates stock_items but does not
            // surface the new available; read it inside the same tx.
            var affectedSkus = expired.Select(e => e.Sku).Distinct().ToArray();
            var perSkuAvailable = await ReadAvailableForSkusAsync(
                connection,
                transaction,
                affectedSkus,
                ct
            ).ConfigureAwait(false);
            foreach (var (sku, available) in perSkuAvailable)
            {
                AppendOutbox(
                    new StockLevelChangedV1(_requestContext.TenantId, sku, available, occurredAt),
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
                Payload = JsonSerializer.Serialize(ev, ev.GetType(), OutboxJsonOptions.Default),
                TraceId = traceId,
                CreatedAt = occurredAt,
            }
        );
    }

    /// <summary>
    /// Sprint-5 U2 / KTD1 — generic outbox append for cross-module integration
    /// events (records in <c>ShopFlow.Contracts.*</c>) that do not implement
    /// <see cref="IDomainEvent"/>. Mirrors the Sprint-3-redux consumer-side
    /// <c>EnqueueOutbox&lt;T&gt;</c> shape so JSON serialization, trace id, and
    /// tenant scope follow the same path.
    /// </summary>
    private void AppendOutbox<T>(T integrationEvent, DateTime occurredAt)
        where T : class
    {
        var traceId = Activity.Current?.TraceId.ToString();
        _db.OutboxMessages.Add(
            new OutboxMessage
            {
                Id = Guid.NewGuid(),
                TenantId = _requestContext.TenantId,
                EventType = typeof(T).AssemblyQualifiedName!,
                Payload = JsonSerializer.Serialize(
                    integrationEvent,
                    typeof(T),
                    OutboxJsonOptions.Default
                ),
                TraceId = traceId,
                CreatedAt = occurredAt,
            }
        );
    }

    /// <summary>
    /// Sprint-5 U2 / KTD1 — read post-commit <c>available</c> for the given
    /// SKU set within the active transaction (read-your-own-writes). Used by
    /// the TryReserveLines + ReleaseExpired paths whose CTEs do not surface
    /// the post-update <c>stock_items.available</c> column.
    /// </summary>
    private static async Task<IReadOnlyDictionary<string, int>> ReadAvailableForSkusAsync(
        NpgsqlConnection connection,
        IDbContextTransaction transaction,
        IReadOnlyCollection<string> skus,
        CancellationToken ct
    )
    {
        if (skus.Count == 0)
        {
            return new Dictionary<string, int>();
        }

        var result = new Dictionary<string, int>(skus.Count);
        await using var cmd = connection.CreateCommand();
        cmd.Transaction = (NpgsqlTransaction)transaction.GetDbTransaction();
        cmd.CommandText = "SELECT sku, available FROM stock_items WHERE sku = ANY(@p_skus)";
        cmd.Parameters.Add(
            new NpgsqlParameter("p_skus", NpgsqlDbType.Array | NpgsqlDbType.Varchar)
            {
                Value = skus.ToArray(),
            }
        );
        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            result[reader.GetString(0)] = reader.GetInt32(1);
        }
        return result;
    }
}
