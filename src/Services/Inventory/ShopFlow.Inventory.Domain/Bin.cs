namespace ShopFlow.Inventory.Domain;

/// <summary>
/// Physical bin / shelf location inside a <see cref="Zone"/>. Carries
/// capacity (max units) and a running <see cref="OccupancyQty"/> updated
/// by the bin-aware <c>AdjustAsync</c> in U5. Per Sprint-2-redux plan R13.
/// </summary>
public sealed class Bin
{
    public long BinId { get; private set; }

    public long ZoneId { get; private set; }

    public string Name { get; private set; } = string.Empty;

    public int Capacity { get; private set; }

    public int OccupancyQty { get; private set; }

    public int AvailableCapacity => Capacity - OccupancyQty;

    private Bin() { }

    public static Bin Create(long zoneId, string name, int capacity)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("bin name is required.", nameof(name));
        }
        if (capacity <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(capacity),
                capacity,
                "capacity must be > 0."
            );
        }
        return new Bin
        {
            ZoneId = zoneId,
            Name = name.Trim(),
            Capacity = capacity,
            OccupancyQty = 0,
        };
    }

    internal void AdjustOccupancy(int delta)
    {
        var next = OccupancyQty + delta;
        if (next < 0)
        {
            throw new InvalidOperationException(
                $"bin {BinId} occupancy underflow: {OccupancyQty} + {delta} < 0."
            );
        }
        OccupancyQty = next;
    }
}
