namespace ShopFlow.Inventory.Application;

/// <summary>
/// Outcome of <see cref="Ports.IReservationRepository.ReleaseLinesAsync"/>
/// per Sprint-3-redux K11. Always-success shape — the WHERE
/// <c>status='Pending'</c> guard means already-released rows silently
/// skip rather than error. The caller emits <c>StockReleasedV1</c> with
/// <see cref="ReleasedLineIds"/> so the saga's Set-based dedup
/// (<c>ReleasedLineSkus</c> per K plan supplementary note) sees the
/// exact set that just transitioned, not a re-emission of the original
/// request set.
/// </summary>
public sealed record ReleaseLinesResult(IReadOnlyList<string> ReleasedLineIds);
