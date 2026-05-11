namespace ShopFlow.SharedKernel.Application;

/// <summary>
/// Per-request ambient context: tenant identity (Id + Slug), the resolved
/// per-tenant database connection string, W3C TraceContext correlation id,
/// and the optional authenticated user. Populated by
/// <see cref="Infrastructure.TenantRoutingMiddleware"/> at the API boundary
/// from header / JWT / subdomain (header &gt; JWT &gt; subdomain priority,
/// conflicts rejected with 403). Code below middleware reads this and
/// trusts it; re-validation in handlers is forbidden by analyzer
/// <c>ShopFlow0004</c>. Per ADR-0003 + AGENTS.md §3.15.
/// </summary>
public interface IRequestContext
{
    /// <summary>
    /// Active tenant for this request. Throws on read when unset.
    /// </summary>
    Guid TenantId { get; }

    /// <summary>
    /// Active tenant's slug (URL-safe short identifier, e.g. <c>"acme-vn"</c>).
    /// Used in logs / traces and for human-readable diagnostics.
    /// </summary>
    string TenantSlug { get; }

    /// <summary>
    /// Resolved per-tenant Postgres connection string (always PgBouncer-fronted
    /// for application traffic). The per-request DbContext factory reads this
    /// to construct DbContexts scoped to the right tenant DB.
    /// </summary>
    string DbConnectionString { get; }

    /// <summary>
    /// W3C TraceContext correlation id for this request.
    /// </summary>
    string CorrelationId { get; }

    /// <summary>
    /// Optional authenticated user id; null for anonymous endpoints
    /// (e.g. webhook receivers gated by HMAC).
    /// </summary>
    Guid? UserId { get; }
}
