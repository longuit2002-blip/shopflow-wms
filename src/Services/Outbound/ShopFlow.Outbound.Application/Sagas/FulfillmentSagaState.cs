using MassTransit;

namespace ShopFlow.Outbound.Application.Sagas;

/// <summary>
/// MassTransit saga instance for <see cref="FulfillmentSaga"/> per
/// Sprint-3-redux U4. One row per in-flight order in the <c>saga_state</c>
/// table on the tenant DB (managed by MassTransit's EF saga repository).
/// </summary>
/// <remarks>
/// <para>The state machine column shape per the U1 migration (in quoted
/// PascalCase so MT's default convention binds without per-column EF
/// configuration):</para>
/// <list type="bullet">
///   <item><description><see cref="CorrelationId"/> — uuid PK; equals <c>OrderId</c> by K2.</description></item>
///   <item><description><see cref="CurrentState"/> — text; the named state ("Created", "AwaitingReservation", ...).</description></item>
///   <item><description><see cref="RowVersion"/> — bytea; optimistic-concurrency token. MT 8.3.4's EF saga repo uses pessimistic <c>SELECT FOR UPDATE</c> (via <c>UsePostgres()</c>) for the actual concurrency primitive; <see cref="RowVersion"/> is still mapped because the migration declares the column and EF would otherwise see it as a "missing property" on the model.</description></item>
///   <item><description><see cref="UpdatedAt"/> — timestamptz; populated by each transition's Then handler for diagnostics.</description></item>
/// </list>
///
/// <para>Per-state context fields (NOT mapped to typed columns yet; MT
/// will create them as additional <c>text</c> / <c>int</c> columns via
/// EF auto-mapping when the saga repo's entity configuration applies).
/// These are flat scalars by design — MT's EF saga repo doesn't support
/// nested objects on the instance type, so a "list of line ids" becomes
/// a comma-separated string column.</para>
/// </remarks>
public sealed class FulfillmentSagaState : SagaStateMachineInstance, ISagaVersion
{
    /// <summary>
    /// Equals the originating <c>OrderId</c> per K2 — MT's
    /// <c>CorrelateById(ctx => ctx.Message.OrderId)</c> binds events back
    /// to this row via the primary key.
    /// </summary>
    public Guid CorrelationId { get; set; }

    /// <summary>
    /// MT's named-state column for <see cref="MassTransitStateMachine{T}.InstanceState(System.Linq.Expressions.Expression{System.Func{T, string}})"/>.
    /// </summary>
    public string CurrentState { get; set; } = string.Empty;

    /// <summary>
    /// EF's optimistic-concurrency token. MT 8.3.4's EF saga repository
    /// uses pessimistic locking via <c>UsePostgres()</c> + <c>SELECT FOR
    /// UPDATE</c>, but the column is still part of the migration so we
    /// map it to keep the model in sync with the schema.
    /// </summary>
    public byte[] RowVersion { get; set; } = Array.Empty<byte>();

    /// <summary>
    /// Wall-clock timestamp of the last state transition; updated by each
    /// transition's Then handler.
    /// </summary>
    public DateTime UpdatedAt { get; set; }

    /// <summary>
    /// Implements <see cref="ISagaVersion.Version"/> for MT 8.3.4 saga
    /// repository concurrency tracking — incremented automatically on
    /// each persistence write.
    /// </summary>
    public int Version { get; set; }

    /// <summary>
    /// Tenant id captured from the initiating <c>OrderPlacedV1</c>. The
    /// saga uses it when re-emitting cross-module commands so the envelope
    /// header is populated correctly for downstream consumers.
    /// </summary>
    public Guid TenantId { get; set; }

    /// <summary>
    /// Shipping profile captured at saga start. Drives the reservation
    /// TTL (U5 reads this when emitting <c>PickRequestV1</c>).
    /// </summary>
    public string ShippingProfile { get; set; } = string.Empty;

    /// <summary>
    /// Number of order lines on the originating order. Carries forward
    /// for compensation accounting.
    /// </summary>
    public int LineCount { get; set; }

    /// <summary>
    /// Comma-separated list of <c>order_line_id</c> values that successfully
    /// reserved (from <c>StockReservedV1</c>'s line outcomes). Empty string
    /// before reservation; populated on the AwaitingReservation → Reserved
    /// transition. U7 reads this to construct <c>ReleaseStockV1</c> for the
    /// compensation path.
    /// </summary>
    public string ReservedLineSkus { get; set; } = string.Empty;

    /// <summary>
    /// Comma-separated list of <c>order_line_id</c> values that have already
    /// produced a <c>StockReleasedV1</c> event. Supports the U7 Set-based
    /// dedup against MassTransit at-least-once redelivery per the K15
    /// supplementary decision — decrement <see cref="LinesAwaitingRelease"/>
    /// only on first sight per line id.
    /// </summary>
    public string ReleasedLineSkus { get; set; } = string.Empty;

    /// <summary>
    /// Counter U7 uses to drive the CompensatingReservation → Cancelled
    /// transition. Initialized from <see cref="ReservedLineSkus"/>'s
    /// item count; decremented on each fresh <c>StockReleasedV1</c>;
    /// when zero, the saga transitions to Cancelled.
    /// </summary>
    public int LinesAwaitingRelease { get; set; }
}
