namespace ShopFlow.SharedKernel.Application.Ports;

/// <summary>
/// Read-only port over the control-plane tenant catalog. Implemented in
/// <c>ShopFlow.ControlPlane.Infrastructure</c> (U5) with an in-memory LRU
/// cache (size 1000, TTL 5 min) over the <c>shopflow_control.tenants</c>
/// table. The routing middleware and the multiplexed outbox dispatcher
/// resolve tenants through this seam — never via direct EF queries against
/// the catalog DB (AGENTS.md §3.19).
/// </summary>
/// <remarks>
/// Cache invalidation is synchronous on write paths inside the catalog
/// implementation (provision-complete, archive-start, tier change). The
/// 5-minute TTL is the fallback for cross-process staleness when multiple
/// app instances run; Phase-2 escalates to Redis-backed pub/sub eviction.
/// </remarks>
public interface ITenantCatalog
{
    /// <summary>
    /// Resolve a tenant by its slug. Returns <c>null</c> for unknown slugs.
    /// </summary>
    Task<TenantInfo?> LookupBySlugAsync(string slug, CancellationToken ct);

    /// <summary>
    /// Resolve a tenant by its id. Returns <c>null</c> when not found.
    /// </summary>
    Task<TenantInfo?> LookupByIdAsync(Guid tenantId, CancellationToken ct);

    /// <summary>
    /// Enumerate every tenant currently in <see cref="TenantStatus.Ready"/>.
    /// Used by the multiplexed outbox dispatcher to fan out per-tenant
    /// batches each tick.
    /// </summary>
    Task<IReadOnlyList<TenantInfo>> GetReadyTenantsAsync(CancellationToken ct);
}
