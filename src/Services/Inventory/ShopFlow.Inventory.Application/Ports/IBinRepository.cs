using ShopFlow.Inventory.Domain;

namespace ShopFlow.Inventory.Application.Ports;

/// <summary>
/// Read-only port over <see cref="Bin"/> + <see cref="Zone"/> joined data
/// per Sprint-2-redux plan U5. Used by the put-away suggestion service
/// to rank candidates.
/// </summary>
public interface IBinRepository
{
    Task<Bin?> FindByIdAsync(long binId, CancellationToken ct);

    Task<IReadOnlyList<Bin>> ListByZoneAsync(long zoneId, CancellationToken ct);
}
