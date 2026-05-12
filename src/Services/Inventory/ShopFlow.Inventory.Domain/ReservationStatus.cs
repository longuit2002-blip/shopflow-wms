namespace ShopFlow.Inventory.Domain;

/// <summary>
/// Reservation lifecycle states per Tech Design v3.0 §4.2.
/// </summary>
/// <remarks>
/// State machine:
/// <list type="bullet">
///   <item><description><c>Pending</c> — reservation written by TryReserve (Sprint-1-redux).</description></item>
///   <item><description><c>Pending</c> → <c>Confirmed</c> on Confirm(orderId): stock leaves the warehouse.</description></item>
///   <item><description><c>Pending</c> → <c>Released</c> on cancellation or TTL expiry.</description></item>
///   <item><description><c>Pending</c> → <c>Expired</c> by <c>ReservationExpiryWorker</c> after the TTL elapses without Confirm.</description></item>
/// </list>
/// <para><c>Confirmed</c> and <c>Released</c> are terminal. <c>Expired</c>
/// is terminal but distinguished from <c>Released</c> so dashboards can
/// surface TTL-driven losses separately from explicit cancellations.</para>
/// </remarks>
public enum ReservationStatus
{
    Pending = 0,
    Confirmed = 1,
    Released = 2,
    Expired = 3,
}
