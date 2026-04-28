namespace ShopFlow.SharedKernel.Application;

/// <summary>
/// Default <see cref="IRequestContext"/> implementation used by the API
/// pipeline. Populated by middleware at the API boundary; reading
/// <see cref="TenantId"/> before assignment throws so the failure mode is
/// loud rather than a silent zero-tenant query.
/// </summary>
public sealed class RequestContext : IRequestContext
{
    private Guid? _tenantId;

    public Guid TenantId
    {
        get =>
            _tenantId
            ?? throw new InvalidOperationException(
                "IRequestContext.TenantId accessed before the request boundary populated it. "
                    + "Ensure the tenant-resolution middleware runs before any handler invocation."
            );
        private set => _tenantId = value;
    }

    public string CorrelationId { get; private set; } = string.Empty;

    public Guid? UserId { get; private set; }

    public void Initialize(Guid tenantId, string correlationId, Guid? userId)
    {
        TenantId = tenantId;
        CorrelationId = correlationId ?? throw new ArgumentNullException(nameof(correlationId));
        UserId = userId;
    }
}
