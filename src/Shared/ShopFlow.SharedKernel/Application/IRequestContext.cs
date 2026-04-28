namespace ShopFlow.SharedKernel.Application;

/// <summary>
/// Per-request ambient context: tenant scope plus W3C TraceContext
/// correlation id. Populated at the API boundary and propagated into
/// <c>SET LOCAL app.tenant_id = '…'</c> by <see cref="Infrastructure.TenancyInterceptor"/>
/// and onto every published integration event by the outbox dispatcher.
/// Per AGENTS.md §3.17, §6.39, §6.40.
/// </summary>
public interface IRequestContext
{
    /// <summary>
    /// Active tenant for this request. Throws on read when unset — callers
    /// should never see <c>Guid.Empty</c> here.
    /// </summary>
    Guid TenantId { get; }

    /// <summary>
    /// W3C TraceContext correlation id for the request, used to stitch
    /// inbound HTTP, outbox events, and downstream service calls into a
    /// single trace.
    /// </summary>
    string CorrelationId { get; }

    /// <summary>
    /// Optional authenticated user id; null for anonymous endpoints
    /// (e.g. webhook receivers gated by HMAC).
    /// </summary>
    Guid? UserId { get; }
}
