namespace ShopFlow.SharedKernel.Application.Ports;

/// <summary>
/// Read-only view of a tenant resolved from the control-plane catalog by
/// <see cref="ITenantCatalog"/>. The routing middleware uses this to populate
/// <see cref="IRequestContext"/>; the multiplexed outbox dispatcher iterates
/// these to fan out per-tenant batches.
/// </summary>
/// <param name="Id">Tenant primary key in <c>shopflow_control.tenants</c>.</param>
/// <param name="Slug">URL-safe short identifier, unique per cluster.</param>
/// <param name="DbName">Postgres database name, unique per cluster.</param>
/// <param name="DbConnectionString">Pre-resolved PgBouncer-fronted connection string for the tenant DB.</param>
/// <param name="Region">Logical region (Phase-3+ residency hint; unused in Phase-0-redux).</param>
/// <param name="Tier">Pricing tier (free, paid, enterprise) — drives quota / noisy-neighbor policy at Phase-2.</param>
/// <param name="Status">Tenant lifecycle status. Only <c>Ready</c> serves traffic.</param>
public sealed record TenantInfo(
    Guid Id,
    string Slug,
    string DbName,
    string DbConnectionString,
    string Region,
    string Tier,
    TenantStatus Status
);

/// <summary>
/// Tenant lifecycle states. Transitions:
/// <c>Pending → Provisioning → (ProvisioningFailed | Ready)</c>;
/// <c>Ready → Archiving → Archived</c>.
/// </summary>
public enum TenantStatus
{
    Pending,
    Provisioning,
    ProvisioningFailed,
    Ready,
    Archiving,
    Archived,
}
