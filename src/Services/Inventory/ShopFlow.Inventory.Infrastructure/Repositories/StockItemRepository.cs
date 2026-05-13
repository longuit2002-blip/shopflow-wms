using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using NpgsqlTypes;
using ShopFlow.Inventory.Application.Ports;
using ShopFlow.Inventory.Domain;
using ShopFlow.SharedKernel.Domain;

namespace ShopFlow.Inventory.Infrastructure.Repositories;

/// <summary>
/// EF Core implementation of <see cref="IStockItemRepository"/>. Sprint-2-redux
/// U5 ships the bin-aware <see cref="AdjustAtBinAsync"/>; the legacy
/// <see cref="AdjustAsync"/> + <see cref="FindBySkuAsync"/> +
/// <see cref="AddAsync"/> remain NIE for Sprint-3-redux (Outbound's
/// picking flow).
/// </summary>
public sealed class StockItemRepository : IStockItemRepository
{
    private readonly InventoryDbContext _db;

    public StockItemRepository(InventoryDbContext db)
    {
        _db = db;
    }

    public Task<StockItem?> FindBySkuAsync(Sku sku, CancellationToken ct)
    {
        _ = (sku, ct, _db);
        throw new NotImplementedException(
            "Sprint-3-redux body — Outbound picking flow needs FindBySkuAsync."
        );
    }

    public Task AddAsync(StockItem item, CancellationToken ct)
    {
        _ = (item, ct);
        throw new NotImplementedException(
            "Sprint-3-redux body — explicit AddAsync from admin workflows; Sprint-2-redux auto-creates via AdjustAtBinAsync."
        );
    }

    public Task<Result> AdjustAsync(
        Sku sku,
        int delta,
        StockAdjustmentReason reason,
        string? note,
        CancellationToken ct
    )
    {
        _ = (sku, delta, reason, note, ct);
        throw new NotImplementedException(
            "Sprint-3-redux body — non-bin adjust path for Outbound's picking flow."
        );
    }

    public async Task<Result> AdjustAtBinAsync(
        Sku sku,
        long binId,
        int delta,
        StockAdjustmentReason reason,
        string? note,
        CancellationToken ct
    )
    {
        ArgumentNullException.ThrowIfNull(sku);
        if (delta == 0)
        {
            return Result.Failure("delta must be non-zero.", "stock.adjustment_zero");
        }

        await using var transaction = await _db
            .Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct)
            .ConfigureAwait(false);

        var connection = (NpgsqlConnection)_db.Database.GetDbConnection();

        try
        {
            // 1. Upsert stock_items: create row at (available=0, reserved=0) if
            //    missing, otherwise leave existing counts alone (we update
            //    available below in the same tx).
            await using (var ensureCmd = connection.CreateCommand())
            {
                ensureCmd.Transaction = (NpgsqlTransaction)transaction.GetDbTransaction();
                ensureCmd.CommandText = """
                    INSERT INTO stock_items (sku, available, reserved, created_at, row_version)
                    VALUES (@p_sku, 0, 0, @p_now, (txid_current())::text::xid)
                    ON CONFLICT (sku) DO NOTHING;
                    """;
                ensureCmd.Parameters.Add(
                    new NpgsqlParameter("p_sku", NpgsqlDbType.Varchar) { Value = sku.Value }
                );
                ensureCmd.Parameters.Add(
                    new NpgsqlParameter("p_now", NpgsqlDbType.TimestampTz)
                    {
                        Value = DateTime.UtcNow,
                    }
                );
                await ensureCmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            }

            // 2. Upsert stock_item_bins: create row at delta if missing, else
            //    add delta to existing quantity. Reject on underflow.
            int newBinQty;
            await using (var binCmd = connection.CreateCommand())
            {
                binCmd.Transaction = (NpgsqlTransaction)transaction.GetDbTransaction();
                binCmd.CommandText = """
                    INSERT INTO stock_item_bins (sku, bin_id, quantity)
                    VALUES (@p_sku, @p_bin, @p_delta)
                    ON CONFLICT (sku, bin_id) DO UPDATE
                        SET quantity = stock_item_bins.quantity + EXCLUDED.quantity
                    RETURNING quantity;
                    """;
                binCmd.Parameters.Add(
                    new NpgsqlParameter("p_sku", NpgsqlDbType.Varchar) { Value = sku.Value }
                );
                binCmd.Parameters.Add(
                    new NpgsqlParameter("p_bin", NpgsqlDbType.Bigint) { Value = binId }
                );
                binCmd.Parameters.Add(
                    new NpgsqlParameter("p_delta", NpgsqlDbType.Integer) { Value = delta }
                );
                var scalar = await binCmd.ExecuteScalarAsync(ct).ConfigureAwait(false);
                newBinQty = Convert.ToInt32(scalar);
            }

            if (newBinQty < 0)
            {
                await transaction.RollbackAsync(ct).ConfigureAwait(false);
                return Result.Failure(
                    $"bin underflow: quantity would become {newBinQty}.",
                    "stock.bin_underflow"
                );
            }

            // 3. Update stock_items.available by delta. Negative delta on an
            //    empty available column trips a CHECK against negative — the
            //    aggregate can't reflect units we don't have. (No explicit
            //    CHECK constraint today; the bin underflow above catches the
            //    common case. Phase-2 can add a stock_items CHECK if needed.)
            await using (var aggCmd = connection.CreateCommand())
            {
                aggCmd.Transaction = (NpgsqlTransaction)transaction.GetDbTransaction();
                aggCmd.CommandText = """
                    UPDATE stock_items
                       SET available  = available + @p_delta,
                           updated_at = @p_now
                     WHERE sku = @p_sku;
                    """;
                aggCmd.Parameters.Add(
                    new NpgsqlParameter("p_sku", NpgsqlDbType.Varchar) { Value = sku.Value }
                );
                aggCmd.Parameters.Add(
                    new NpgsqlParameter("p_delta", NpgsqlDbType.Integer) { Value = delta }
                );
                aggCmd.Parameters.Add(
                    new NpgsqlParameter("p_now", NpgsqlDbType.TimestampTz)
                    {
                        Value = DateTime.UtcNow,
                    }
                );
                await aggCmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            }

            // 4. Update bin occupancy by delta.
            await using (var binOccCmd = connection.CreateCommand())
            {
                binOccCmd.Transaction = (NpgsqlTransaction)transaction.GetDbTransaction();
                binOccCmd.CommandText = """
                    UPDATE bins
                       SET occupancy_qty = occupancy_qty + @p_delta
                     WHERE bin_id = @p_bin;
                    """;
                binOccCmd.Parameters.Add(
                    new NpgsqlParameter("p_bin", NpgsqlDbType.Bigint) { Value = binId }
                );
                binOccCmd.Parameters.Add(
                    new NpgsqlParameter("p_delta", NpgsqlDbType.Integer) { Value = delta }
                );
                await binOccCmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            }

            // 5. Audit row.
            await using (var adjCmd = connection.CreateCommand())
            {
                adjCmd.Transaction = (NpgsqlTransaction)transaction.GetDbTransaction();
                adjCmd.CommandText = """
                    INSERT INTO stock_adjustments
                        (id, sku, delta, reason, note, created_at)
                    VALUES (@p_id, @p_sku, @p_delta, @p_reason, @p_note, @p_now);
                    """;
                adjCmd.Parameters.Add(
                    new NpgsqlParameter("p_id", NpgsqlDbType.Uuid) { Value = Guid.NewGuid() }
                );
                adjCmd.Parameters.Add(
                    new NpgsqlParameter("p_sku", NpgsqlDbType.Varchar) { Value = sku.Value }
                );
                adjCmd.Parameters.Add(
                    new NpgsqlParameter("p_delta", NpgsqlDbType.Integer) { Value = delta }
                );
                adjCmd.Parameters.Add(
                    new NpgsqlParameter("p_reason", NpgsqlDbType.Varchar)
                    {
                        Value = reason.ToString(),
                    }
                );
                adjCmd.Parameters.Add(
                    new NpgsqlParameter("p_note", NpgsqlDbType.Varchar)
                    {
                        Value = (object?)note ?? DBNull.Value,
                    }
                );
                adjCmd.Parameters.Add(
                    new NpgsqlParameter("p_now", NpgsqlDbType.TimestampTz)
                    {
                        Value = DateTime.UtcNow,
                    }
                );
                await adjCmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            }

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
}
