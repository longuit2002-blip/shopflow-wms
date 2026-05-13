namespace ShopFlow.Inbound.Domain;

/// <summary>
/// Reconciliation ticket lifecycle per Sprint-2-redux plan R9. Sprint-2-redux
/// ships <c>Open</c> only; <c>Resolved</c> + <c>Cancelled</c> exist on the
/// enum so the schema is forward-compatible when the resolution workflow
/// lands (Sprint-2.5 or Phase-2). The brainstorm doc lists ticket resolution
/// as deferred to follow-up.
/// </summary>
public enum ReconciliationTicketStatus
{
    Open = 0,
    Resolved = 1,
    Cancelled = 2,
}
