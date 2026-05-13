using Microsoft.EntityFrameworkCore;
using ShopFlow.Outbound.Application.Ports;
using ShopFlow.Outbound.Domain;

namespace ShopFlow.Outbound.Infrastructure.Repositories;

/// <summary>
/// EF Core implementation of <see cref="IPickerRepository"/>. Returns
/// the picker pool ordered by <c>picker_id</c> so the wave generator's
/// round-robin cursor is deterministic across ticks (plan U5 test
/// scenario 4 relies on stable ordering).
/// </summary>
public sealed class PickerRepository : IPickerRepository
{
    private readonly OutboundDbContext _db;

    public PickerRepository(OutboundDbContext db)
    {
        _db = db;
    }

    public async Task<IReadOnlyList<Picker>> ListByTenantAsync(CancellationToken ct)
    {
        var rows = await _db
            .Pickers.AsNoTracking()
            .OrderBy(p => p.PickerId)
            .ToListAsync(ct)
            .ConfigureAwait(false);
        return rows;
    }
}
