namespace ShopFlow.Inventory.Application.Ports;

/// <summary>
/// Read-only put-away suggestion service per Sprint-2-redux plan R16.
/// Given a SKU + qty request, ranks the top-K bin candidates by
/// (zone-priority, available capacity DESC, current occupancy ASC, bin
/// name lex ASC for tiebreak). Sprint-2-redux returns top-3.
/// </summary>
public interface IPutAwaySuggestionService
{
    Task<IReadOnlyList<PutAwayCandidate>> GetTopCandidatesAsync(
        string sku,
        int requestedQty,
        int topK,
        CancellationToken ct
    );
}

public sealed record PutAwayCandidate(
    long BinId,
    string BinName,
    long ZoneId,
    string ZoneName,
    int AvailableCapacity,
    int CurrentOccupancy,
    bool IsHomeZone
);
