using ShopFlow.SharedKernel.Domain;

namespace ShopFlow.ControlPlane.Domain;

/// <summary>
/// Audit row for the control-plane <c>tenant_events</c> table per Tech
/// Design v3.0 §1.5. One row per material lifecycle event: provisioning
/// requested, provisioning failed, archive scheduled, breach notified,
/// routing conflict (referenced by <c>TenantRoutingMiddleware</c>). The
/// payload is a free-form JSON blob; the canonical schema per event-type
/// lives in <c>docs/redesign/02-technical-design-document.md</c>.
/// </summary>
/// <remarks>
/// This is intentionally not a <c>BaseEntity</c>-derived aggregate. The
/// rows are append-only audit records owned by the Tenant aggregate's
/// transaction boundary — see <c>ControlPlaneDbContext</c> for the EF
/// configuration.
/// </remarks>
public sealed class TenantEvent
{
    public Guid Id { get; private set; } = Guid.NewGuid();

    public Guid TenantId { get; private set; }

    public string EventType { get; private set; } = string.Empty;

    public string PayloadJson { get; private set; } = "{}";

    public DateTime OccurredAt { get; private set; } = DateTime.UtcNow;

    private TenantEvent() { }

    public static TenantEvent Record(Guid tenantId, string eventType, string payloadJson) =>
        new()
        {
            TenantId = tenantId,
            EventType = eventType,
            PayloadJson = string.IsNullOrWhiteSpace(payloadJson) ? "{}" : payloadJson,
            OccurredAt = DateTime.UtcNow,
        };
}
