using ShopFlow.Inbound.Domain;

namespace ShopFlow.Inbound.Application.Ports;

/// <summary>
/// Write + read surface for the append-only
/// <see cref="ReconciliationTicket"/> log per Sprint-2-redux plan R9.
/// Sprint-2-redux ships Open-status creation only; resolution flow is
/// deferred.
/// </summary>
public interface IReconciliationTicketRepository
{
    Task AddAsync(ReconciliationTicket ticket, CancellationToken ct);

    Task<IReadOnlyList<ReconciliationTicket>> ListOpenAsync(CancellationToken ct);
}
