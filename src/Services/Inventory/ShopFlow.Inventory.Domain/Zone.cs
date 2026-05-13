using ShopFlow.SharedKernel.Domain;

namespace ShopFlow.Inventory.Domain;

/// <summary>
/// Logical area within a warehouse — receiving zone, fast-mover pick zone,
/// reserve zone, etc. Per Sprint-2-redux plan R13. Zones are reference data
/// (operator-seeded); no state machine in this sprint.
/// </summary>
public sealed class Zone
{
    public long ZoneId { get; private set; }

    public string Name { get; private set; } = string.Empty;

    public string WarehouseId { get; private set; } = string.Empty;

    private Zone() { }

    public static Zone Create(string name, string warehouseId)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("zone name is required.", nameof(name));
        }
        if (string.IsNullOrWhiteSpace(warehouseId))
        {
            throw new ArgumentException("warehouse_id is required.", nameof(warehouseId));
        }
        return new Zone { Name = name.Trim(), WarehouseId = warehouseId.Trim() };
    }
}
