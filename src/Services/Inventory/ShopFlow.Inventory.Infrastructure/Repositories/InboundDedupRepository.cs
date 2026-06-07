using Microsoft.EntityFrameworkCore;
using Npgsql;
using ShopFlow.Inventory.Application.Ports;
using ShopFlow.Inventory.Domain;

namespace ShopFlow.Inventory.Infrastructure.Repositories;

/// <summary>
/// EF Core implementation of <see cref="IInboundDedupRepository"/>. Uses
/// <see cref="InboundDedup.Record"/> + EF Add to capture the dedup row;
/// catches <c>23505</c> on SaveChanges to detect duplicate delivery.
/// </summary>
public sealed class InboundDedupRepository : IInboundDedupRepository
{
    private readonly InventoryDbContext _db;

    public InboundDedupRepository(InventoryDbContext db)
    {
        _db = db;
    }

    public async Task<bool> TryRecordAsync(
        Guid receivingId,
        Guid lineId,
        string sku,
        int quantity,
        DateTime processedAt,
        CancellationToken ct
    )
    {
        var row = InboundDedup.Record(receivingId, lineId, sku, quantity, processedAt);
        await _db.InboundDedup.AddAsync(row, ct).ConfigureAwait(false);

        try
        {
            await _db.SaveChangesAsync(ct).ConfigureAwait(false);
            return true;
        }
        catch (DbUpdateException ex)
            when (ex.InnerException is PostgresException pg
                && pg.SqlState == PostgresErrorCodes.UniqueViolation
            )
        {
            // Duplicate redelivery — detach the conflicting entry so the
            // DbContext stays usable for the caller's ACK path.
            _db.Entry(row).State = EntityState.Detached;
            return false;
        }
    }
}
