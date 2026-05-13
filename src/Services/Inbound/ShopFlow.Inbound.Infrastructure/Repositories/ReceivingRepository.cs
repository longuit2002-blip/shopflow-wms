using Microsoft.EntityFrameworkCore;
using ShopFlow.Inbound.Application.Ports;
using ShopFlow.Inbound.Domain;

namespace ShopFlow.Inbound.Infrastructure.Repositories;

/// <summary>
/// EF Core implementation of <see cref="IReceivingRepository"/>. Eager-
/// loads the <see cref="Receiving.Lines"/> child collection so handlers
/// can apply additional confirmations without an N+1 round trip.
/// </summary>
public sealed class ReceivingRepository : IReceivingRepository
{
    private readonly InboundDbContext _db;

    public ReceivingRepository(InboundDbContext db)
    {
        _db = db;
    }

    public async Task AddAsync(Receiving receiving, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(receiving);
        await _db.Receivings.AddAsync(receiving, ct).ConfigureAwait(false);
    }

    public Task<Receiving?> FindByIdAsync(Guid id, CancellationToken ct)
    {
        return _db.Receivings.Include(r => r.Lines).FirstOrDefaultAsync(r => r.Id == id, ct);
    }
}
