namespace ShopFlow.SharedKernel.Domain;

/// <summary>
/// Tenant lifecycle states per ADR-0003 and Tech Design v3.0 §1.5 / §2.
/// Allowed transitions:
/// <c>Pending → Provisioning → (ProvisioningFailed | Ready)</c>;
/// <c>Ready → Archiving → Archived</c>;
/// <c>ProvisioningFailed → Provisioning</c> (idempotent retry).
/// </summary>
/// <remarks>
/// Lives in <see cref="ShopFlow.SharedKernel.Domain"/> rather than under
/// <c>Application.Ports</c> so that <see cref="ControlPlane.Domain.Tenant"/>
/// (U5) and the cross-cutting <see cref="Application.Ports.TenantInfo"/>
/// projection (U4) share one enum without either side taking a backward
/// dependency. Pure value type, no framework refs — satisfies AGENTS.md §2.9.
/// </remarks>
public enum TenantStatus
{
    Pending,
    Provisioning,
    ProvisioningFailed,
    Ready,
    Archiving,
    Archived,
}
