namespace ShopFlow.Outbound.Domain;

/// <summary>
/// Reference-data record for warehouse pickers per Sprint-3-redux plan
/// R10. Round-robin assignment in U5 reads <see cref="PickerId"/> ordered
/// by string. Operator-seeded for MVP; load tests seed 5 pickers per
/// tenant via raw SQL. Phase-3+ adds workload tracking columns.
/// </summary>
/// <remarks>
/// U1 ships the type shape. Not a domain aggregate — no behavior, just
/// reference data the assignment service reads.
/// </remarks>
public sealed class Picker
{
    public string PickerId { get; private set; } = string.Empty;

    public string DisplayName { get; private set; } = string.Empty;

    private Picker() { }
}
