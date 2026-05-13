using Microsoft.EntityFrameworkCore;
using ShopFlow.Inbound.Application.Ports;
using ShopFlow.Inbound.Domain;

namespace ShopFlow.Inbound.Infrastructure.Repositories;

/// <summary>
/// EF Core implementation of <see cref="IReconciliationTicketRepository"/>.
/// Append-only writes; reads return open tickets ordered by occurrence
/// (Phase-2 resolution workflow's primary read path).
/// </summary>
public sealed class ReconciliationTicketRepository : IReconciliationTicketRepository
{
    private readonly InboundDbContext _db;

    public ReconciliationTicketRepository(InboundDbContext db)
    {
        _db = db;
    }

    public async Task AddAsync(ReconciliationTicket ticket, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(ticket);
        await _db.ReconciliationTickets.AddAsync(ticket, ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<ReconciliationTicket>> ListOpenAsync(CancellationToken ct)
    {
        var rows = await _db
            .ReconciliationTickets.AsNoTracking()
            .Where(t => t.Status == ReconciliationTicketStatus.Open)
            .OrderBy(t => t.OccurredAt)
            .ToListAsync(ct)
            .ConfigureAwait(false);
        return rows;
    }
}
