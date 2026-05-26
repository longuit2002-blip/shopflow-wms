using System.Data;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;
using ShopFlow.Inventory.Application.Ports;

namespace ShopFlow.Inventory.Infrastructure.Services;

/// <summary>
/// Read-side implementation of <see cref="IPutAwaySuggestionService"/>.
/// Joins <c>bins</c> + <c>zones</c> against the SKU's
/// <c>stock_items.home_zone_id</c>, filters bins with available capacity
/// &gt;= requested qty, and ranks per Sprint-2-redux plan R16:
/// <list type="number">
///   <item><description>bins in the SKU's home zone rank first (zone_priority DESC, where 1 = home, 0 = other);</description></item>
///   <item><description>then by available_capacity DESC;</description></item>
///   <item><description>then by current occupancy ASC;</description></item>
///   <item><description>finally by bin name lex ASC (tiebreaker).</description></item>
/// </list>
/// </summary>
public sealed class PutAwaySuggestionService : IPutAwaySuggestionService
{
    private readonly InventoryDbContext _db;

    public PutAwaySuggestionService(InventoryDbContext db)
    {
        _db = db;
    }

    public async Task<IReadOnlyList<PutAwayCandidate>> GetTopCandidatesAsync(
        string sku,
        int requestedQty,
        int topK,
        CancellationToken ct
    )
    {
        if (string.IsNullOrWhiteSpace(sku))
        {
            throw new ArgumentException("sku is required.", nameof(sku));
        }
        if (requestedQty <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(requestedQty),
                requestedQty,
                "requestedQty must be > 0."
            );
        }
        if (topK <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(topK), topK, "topK must be > 0.");
        }

        var connection = (NpgsqlConnection)_db.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open)
        {
            await connection.OpenAsync(ct).ConfigureAwait(false);
        }

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            SELECT b.bin_id,
                   b.name AS bin_name,
                   z.zone_id,
                   z.name AS zone_name,
                   (b.capacity - b.occupancy_qty) AS available_capacity,
                   b.occupancy_qty,
                   (CASE WHEN si.home_zone_id IS NOT NULL AND si.home_zone_id = z.zone_id
                         THEN 1 ELSE 0 END) AS zone_priority
              FROM bins b
              JOIN zones z ON z.zone_id = b.zone_id
              LEFT JOIN stock_items si ON si.sku = @p_sku
             WHERE (b.capacity - b.occupancy_qty) >= @p_qty
             ORDER BY zone_priority DESC,
                      available_capacity DESC,
                      b.occupancy_qty ASC,
                      b.name ASC
             LIMIT @p_top;
            """;
        cmd.Parameters.Add(new NpgsqlParameter("p_sku", NpgsqlDbType.Varchar) { Value = sku });
        cmd.Parameters.Add(
            new NpgsqlParameter("p_qty", NpgsqlDbType.Integer) { Value = requestedQty }
        );
        cmd.Parameters.Add(new NpgsqlParameter("p_top", NpgsqlDbType.Integer) { Value = topK });

        var candidates = new List<PutAwayCandidate>();
        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            candidates.Add(
                new PutAwayCandidate(
                    BinId: reader.GetInt64(0),
                    BinName: reader.GetString(1),
                    ZoneId: reader.GetInt64(2),
                    ZoneName: reader.GetString(3),
                    AvailableCapacity: reader.GetInt32(4),
                    CurrentOccupancy: reader.GetInt32(5),
                    IsHomeZone: reader.GetInt32(6) == 1
                )
            );
        }
        return candidates;
    }
}
