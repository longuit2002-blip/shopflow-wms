using System.Diagnostics;
using MediatR;

namespace ShopFlow.SharedKernel.Application.Behaviors;

/// <summary>
/// MediatR pipeline behavior: opens an OpenTelemetry activity span for
/// each handled request so handler-level work shows up in traces with
/// tenant id and correlation id as span tags. Per AGENTS.md §6.40, the
/// W3C TraceContext correlation id propagates onto every published
/// integration event from this same span.
/// </summary>
public sealed class TracingBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    /// <summary>
    /// ActivitySource name used to register the kernel's tracer. Modules
    /// that want to emit additional spans should reference this constant
    /// rather than redeclaring the source name.
    /// </summary>
    public const string ActivitySourceName = "ShopFlow.SharedKernel";

    private static readonly ActivitySource Source = new(ActivitySourceName);

    private readonly IRequestContext _requestContext;

    public TracingBehavior(IRequestContext requestContext)
    {
        _requestContext = requestContext;
    }

    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken
    )
    {
        using var activity = Source.StartActivity(
            $"Handle {typeof(TRequest).Name}",
            ActivityKind.Internal
        );

        if (activity is not null)
        {
            try
            {
                activity.SetTag("shopflow.tenant_id", _requestContext.TenantId.ToString());
            }
            catch (InvalidOperationException)
            {
                activity.SetTag("shopflow.tenant_id", "(unset)");
            }

            activity.SetTag("shopflow.correlation_id", _requestContext.CorrelationId);
        }

        try
        {
            return await next().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            throw;
        }
    }
}
