using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using NpgsqlTypes;
using ShopFlow.Inventory.Application.Ports;
using ShopFlow.Inventory.Domain;

namespace ShopFlow.Inventory.Infrastructure.Repositories;

/// <summary>
/// EF Core + raw-SQL implementation of <see cref="IStockItemBinRepository"/>.
/// <see cref="UpsertQuantityAsync"/> uses Postgres' <c>ON CONFLICT</c>
/// upsert so the per-bin row is created on first receive and incremented
/// thereafter — all in a single statement so the running quantity is
/// returned without a separate SELECT.
/// </summary>
public sealed class StockItemBinRepository : IStockItemBinRepository
{
    private readonly InventoryDbContext _db;

    public StockItemBinRepository(InventoryDbContext db)
    {
        _db = db;
    }

    public Task<StockItemBin?> FindBySkuBinAsync(string sku, long binId, CancellationToken ct) =>
        _db.StockItemBins.FirstOrDefaultAsync(s => s.Sku == sku && s.BinId == binId, ct);

    public async Task<int> UpsertQuantityAsync(
        string sku,
        long binId,
        int delta,
        CancellationToken ct
    )
    {
        ArgumentNullException.ThrowIfNull(sku);

        var connection = (NpgsqlConnection)_db.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open)
        {
            await connection.OpenAsync(ct).ConfigureAwait(false);
        }

        await using var cmd = connection.CreateCommand();
        // Join any ambient EF transaction so the upsert participates in
        // the caller's atomic write boundary.
        var currentTx = _db.Database.CurrentTransaction;
        if (currentTx is not null)
        {
            cmd.Transaction = (NpgsqlTransaction)currentTx.GetDbTransaction();
        }

        cmd.CommandText = """
            INSERT INTO stock_item_bins (sku, bin_id, quantity)
            VALUES (@p_sku, @p_bin, @p_delta)
            ON CONFLICT (sku, bin_id) DO UPDATE
                SET quantity = stock_item_bins.quantity + EXCLUDED.quantity
            RETURNING quantity;
            """;
        cmd.Parameters.Add(new NpgsqlParameter("p_sku", NpgsqlDbType.Varchar) { Value = sku });
        cmd.Parameters.Add(new NpgsqlParameter("p_bin", NpgsqlDbType.Bigint) { Value = binId });
        cmd.Parameters.Add(new NpgsqlParameter("p_delta", NpgsqlDbType.Integer) { Value = delta });

        var scalar = await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false);
        return Convert.ToInt32(scalar);
    }
}
