using ShopFlow.Inbound.Domain;

namespace ShopFlow.Inbound.Application.Ports;

/// <summary>
/// Write + read surface for the <see cref="Receiving"/> aggregate per
/// Sprint-2-redux plan R4-R5.
/// </summary>
public interface IReceivingRepository
{
    Task AddAsync(Receiving receiving, CancellationToken ct);

    Task<Receiving?> FindByIdAsync(Guid id, CancellationToken ct);
}
