namespace ShopFlow.Inventory.Application;

/// <summary>
/// Knobs for the Inventory module. Bound from the
/// <c>"Inventory"</c> configuration section by
/// <c>InventoryServiceCollectionExtensions.AddInventoryModule</c>.
/// </summary>
public sealed class InventoryOptions
{
    public const string SectionName = "Inventory";

    /// <summary>
    /// Period between <c>ReservationExpiryWorker</c> ticks (seconds).
    /// Each tick iterates every <c>Ready</c> tenant from the catalog and
    /// runs <c>ReleaseExpiredAsync</c> once per tenant. Defaults to 30s
    /// per Tech Design v3.0 §4.5; bring it lower in tests to make worker
    /// behavior observable inside the test timeout.
    /// </summary>
    public int ExpiryPollIntervalSeconds { get; set; } = 30;

    /// <summary>
    /// Maximum reservations the expiry worker releases per tenant per
    /// tick. Bounds the per-tenant work so a backlog in one tenant
    /// cannot starve the rest. Per Tech Design v3.0 §4.5.
    /// </summary>
    public int ExpiryBatchSize { get; set; } = 200;

    /// <summary>
    /// Default reservation TTL — how long a Pending reservation lives
    /// before the expiry worker is eligible to flip it to Expired.
    /// Tech Design v3.0 §4.2 defaults to 15 minutes; per-channel
    /// overrides land in Phase-2.
    /// </summary>
    public int DefaultReservationTtlMinutes { get; set; } = 15;
}
