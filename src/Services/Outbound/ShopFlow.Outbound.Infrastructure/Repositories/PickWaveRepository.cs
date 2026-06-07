using Microsoft.EntityFrameworkCore;
using ShopFlow.Outbound.Application.Ports;
using ShopFlow.Outbound.Domain;

namespace ShopFlow.Outbound.Infrastructure.Repositories;

/// <summary>
/// EF Core implementation of <see cref="IPickWaveRepository"/>.
/// Eager-loads child <see cref="PickAssignment"/>s on
/// <see cref="FindByIdAsync"/> so the U5 integration tests + diagnostic
/// reads materialise the full aggregate without N+1 round trips.
/// </summary>
public sealed class PickWaveRepository : IPickWaveRepository
{
    private readonly OutboundDbContext _db;

    public PickWaveRepository(OutboundDbContext db)
    {
        _db = db;
    }

    public async Task AddAsync(PickWave wave, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(wave);
        await _db.PickWaves.AddAsync(wave, ct).ConfigureAwait(false);
    }

    public Task<PickWave?> FindByIdAsync(Guid id, CancellationToken ct)
    {
        return _db.PickWaves.Include(w => w.Assignments).FirstOrDefaultAsync(w => w.Id == id, ct);
    }
}
