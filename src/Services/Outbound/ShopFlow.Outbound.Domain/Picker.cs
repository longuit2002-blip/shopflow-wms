namespace ShopFlow.Outbound.Domain;

/// <summary>
/// Reference-data record for warehouse pickers per Sprint-3-redux plan
/// R10. Round-robin assignment in U5 reads <see cref="PickerId"/> ordered
/// by string. Operator-seeded for MVP; load tests seed 5 pickers per
/// tenant via raw SQL. Phase-3+ adds workload tracking columns.
/// </summary>
/// <remarks>
/// Not a domain aggregate — pure reference data the assignment service
/// reads. The factory exists so test code and seed scripts construct
/// pickers without reflection over the private ctor.
/// </remarks>
public sealed class Picker
{
    public string PickerId { get; private set; } = string.Empty;

    public string DisplayName { get; private set; } = string.Empty;

    private Picker() { }

    /// <summary>
    /// Build a picker reference-data row.
    /// </summary>
    public static Picker Create(string pickerId, string displayName)
    {
        if (string.IsNullOrWhiteSpace(pickerId))
        {
            throw new ArgumentException("picker_id is required.", nameof(pickerId));
        }
        if (string.IsNullOrWhiteSpace(displayName))
        {
            throw new ArgumentException("display_name is required.", nameof(displayName));
        }
        return new Picker { PickerId = pickerId.Trim(), DisplayName = displayName.Trim() };
    }
}
