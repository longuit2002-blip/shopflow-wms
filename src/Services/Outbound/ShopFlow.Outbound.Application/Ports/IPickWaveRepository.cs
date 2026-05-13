using ShopFlow.Outbound.Domain;

namespace ShopFlow.Outbound.Application.Ports;

/// <summary>
/// Write + read surface for the <see cref="PickWave"/> aggregate per
/// Sprint-3-redux U5. Closed waves materialise via
/// <see cref="AddAsync"/>; <see cref="FindByIdAsync"/> supports the U5
/// integration test + diagnostic surfaces.
/// </summary>
/// <remarks>
/// Per AGENTS.md §3.16 EF queries route through tenant-scoped
/// repositories; no raw <c>DbSet</c> access in Application or Api
/// (<c>ShopFlow0001</c> enforces).
/// </remarks>
public interface IPickWaveRepository
{
    Task AddAsync(PickWave wave, CancellationToken ct);

    Task<PickWave?> FindByIdAsync(Guid id, CancellationToken ct);
}
