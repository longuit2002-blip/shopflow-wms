using System.Data;
using System.Diagnostics;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage; // GetDbTransaction() extension on IDbContextTransaction
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
/// Reservation-ledger repository. The hot-path
/// <see cref="TryReserveAsync"/> implements the conditional INSERT CTE
/// from Tech Design §7.2 verbatim, wrapped in a <c>SERIALIZABLE</c>
/// transaction. Per AGENTS.md §3.16 this is the only legitimate place
/// in the Inventory module that runs raw SQL against
/// <c>reservations_ledger</c>.
/// </summary>
/// <remarks>
/// The CTE produces one row when
/// <c>total_qty − allocated_qty − sum(active reservations) ≥ requested</c>
/// and zero rows otherwise — the entire correctness invariant lives in
/// the <c>WHERE</c> clause on the INSERT, which Postgres MVCC + the
/// SERIALIZABLE isolation gives us safely under concurrent inserts. Do
/// not "simplify" to a SELECT-then-INSERT pattern; that's exactly the
/// race the conditional INSERT closes.
/// </remarks>
public sealed class ReservationRepository : IReservationRepository
{
    private static readonly JsonSerializerOptions OutboxSerializerOptions = new()
    {
        WriteIndented = false,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly InventoryDbContext _db;
    private readonly IRequestContext _requestContext;
    private readonly TimeProvider _clock;

    public ReservationRepository(
        InventoryDbContext db,
        IRequestContext requestContext,
        TimeProvider clock
    )
    {
        _db = db;
        _requestContext = requestContext;
        _clock = clock;
    }

    public async Task<Result<Guid>> TryReserveAsync(
        Guid tenantId,
        Sku sku,
        int qty,
        Guid orderId,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(sku);
        if (qty <= 0)
        {
            return Result<Guid>.Failure("Reservation qty must be positive.", "INVALID_QTY");
        }

        // Tech Design §7.2 verbatim: conditional INSERT inside a CTE that
        // computes available stock. The RETURNING clause yields the new
        // reservation id when the WHERE on the INSERT is satisfied; zero
        // rows means oversold.
        const string sql = """
            WITH current AS (
                SELECT total_qty, allocated_qty,
                       (SELECT COALESCE(SUM(qty), 0)
                          FROM reservations_ledger
                         WHERE tenant_id = @p_tenant AND sku = @p_sku
                           AND status = 'Active') AS reserved_qty
                  FROM stock_items
                 WHERE tenant_id = @p_tenant AND sku = @p_sku
            )
            INSERT INTO reservations_ledger
                (id, tenant_id, sku, qty, order_id, status, reserved_at, expires_at)
            SELECT @p_id, @p_tenant, @p_sku, @p_qty, @p_order, 'Active',
                   @p_now, @p_expires
              FROM current
             WHERE current.total_qty - current.allocated_qty - current.reserved_qty >= @p_qty
            RETURNING id;
            """;

        var reservationId = Guid.NewGuid();
        var nowUtc = _clock.GetUtcNow().UtcDateTime;
        var expiresAt = nowUtc.AddMinutes(15);

        await using var transaction = await _db
            .Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken)
            .ConfigureAwait(false);

        var connection = (NpgsqlConnection)_db.Database.GetDbConnection();

        Guid? insertedId;
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = sql;
            command.Transaction = (NpgsqlTransaction)transaction.GetDbTransaction();

            command.Parameters.Add(
                new NpgsqlParameter("@p_tenant", NpgsqlDbType.Uuid) { Value = tenantId }
            );
            command.Parameters.Add(
                new NpgsqlParameter("@p_sku", NpgsqlDbType.Varchar) { Value = sku.Value }
            );
            command.Parameters.Add(
                new NpgsqlParameter("@p_qty", NpgsqlDbType.Integer) { Value = qty }
            );
            command.Parameters.Add(
                new NpgsqlParameter("@p_order", NpgsqlDbType.Uuid) { Value = orderId }
            );
            command.Parameters.Add(
                new NpgsqlParameter("@p_id", NpgsqlDbType.Uuid) { Value = reservationId }
            );
            command.Parameters.Add(
                new NpgsqlParameter("@p_now", NpgsqlDbType.TimestampTz) { Value = nowUtc }
            );
            command.Parameters.Add(
                new NpgsqlParameter("@p_expires", NpgsqlDbType.TimestampTz) { Value = expiresAt }
            );

            try
            {
                var scalar = await command
                    .ExecuteScalarAsync(cancellationToken)
                    .ConfigureAwait(false);
                insertedId = scalar is null || scalar is DBNull ? null : (Guid)scalar;
            }
            catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.UniqueViolation)
            {
                // Concurrent re-issue of the same (tenant, order_id):
                // application handler's idempotency lookup raced. Roll back
                // and return the existing row's id.
                await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
                var existing = await FindByOrderIdAsync(tenantId, orderId, cancellationToken)
                    .ConfigureAwait(false);
                return existing is not null
                    ? Result<Guid>.Success(existing.Id)
                    : Result<Guid>.Failure(
                        "Idempotency conflict but no existing reservation found.",
                        "IDEMPOTENCY_CONFLICT"
                    );
            }
        }

        if (insertedId is null)
        {
            await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
            return Result<Guid>.Failure("oversold", "OVERSOLD");
        }

        // Atomically append the StockReserved event to outbox_messages in
        // the same transaction. Per AGENTS.md §6.35 the canonical path is
        // "raise domain event → outbox row → dispatcher"; here the raw-SQL
        // INSERT bypassed the change tracker so we add the outbox row by
        // hand rather than relying on OutboxInterceptor.
        var domainEvent = new StockReservedEvent(
            TenantId: tenantId,
            Sku: sku.Value,
            Quantity: qty,
            ReservationId: insertedId.Value,
            OrderId: orderId,
            OccurredAt: nowUtc
        );

        var outboxRow = new OutboxMessage
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            EventType = typeof(StockReservedEvent).AssemblyQualifiedName!,
            Payload = JsonSerializer.Serialize(domainEvent, OutboxSerializerOptions),
            TraceId = Activity.Current?.TraceId.ToString(),
            CreatedAt = nowUtc,
            RetryCount = 0,
        };

        _db.OutboxMessages.Add(outboxRow);
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);

        return Result<Guid>.Success(insertedId.Value);
    }

    public async Task<Reservation?> FindByOrderIdAsync(
        Guid tenantId,
        Guid orderId,
        CancellationToken cancellationToken
    )
    {
        return await _db
            .Reservations.AsNoTracking()
            .FirstOrDefaultAsync(
                r => r.TenantId == tenantId && r.OrderId == orderId,
                cancellationToken
            )
            .ConfigureAwait(false);
    }

    public async Task<int> ReleaseExpiredAsync(CancellationToken cancellationToken)
    {
        var nowUtc = _clock.GetUtcNow().UtcDateTime;

        // Bulk transition active → expired for rows past their TTL. Tech
        // Design §7.4 emits a StockReleased event per affected row; the
        // dispatcher reads them from outbox_messages. The per-row event
        // emission is left to a follow-up plan unit (the W3 expiry-worker
        // BackgroundService) so that this method stays a single statement
        // for predictable scaling under high active-reservation counts.
        const string sql = """
            UPDATE reservations_ledger
               SET status = 'Expired', finalized_at = @p_now
             WHERE status = 'Active' AND expires_at < @p_now
                   AND tenant_id = @p_tenant;
            """;

        return await _db
            .Database.ExecuteSqlRawAsync(
                sql,
                new[]
                {
                    new NpgsqlParameter("@p_now", NpgsqlDbType.TimestampTz) { Value = nowUtc },
                    new NpgsqlParameter("@p_tenant", NpgsqlDbType.Uuid)
                    {
                        Value = _requestContext.TenantId,
                    },
                },
                cancellationToken
            )
            .ConfigureAwait(false);
    }

    public async Task ConfirmAsync(Guid reservationId, CancellationToken cancellationToken)
    {
        var nowUtc = _clock.GetUtcNow().UtcDateTime;
        const string sql = """
            UPDATE reservations_ledger
               SET status = 'Confirmed', finalized_at = @p_now
             WHERE id = @p_id AND tenant_id = @p_tenant AND status = 'Active';
            """;

        await _db
            .Database.ExecuteSqlRawAsync(
                sql,
                new[]
                {
                    new NpgsqlParameter("@p_id", NpgsqlDbType.Uuid) { Value = reservationId },
                    new NpgsqlParameter("@p_tenant", NpgsqlDbType.Uuid)
                    {
                        Value = _requestContext.TenantId,
                    },
                    new NpgsqlParameter("@p_now", NpgsqlDbType.TimestampTz) { Value = nowUtc },
                },
                cancellationToken
            )
            .ConfigureAwait(false);
    }
}
