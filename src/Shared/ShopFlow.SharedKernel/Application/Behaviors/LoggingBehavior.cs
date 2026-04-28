using System.Diagnostics;
using MediatR;
using Microsoft.Extensions.Logging;

namespace ShopFlow.SharedKernel.Application.Behaviors;

/// <summary>
/// MediatR pipeline behavior: emits a structured "request handled" log line
/// per command/query with tenant id, correlation id, and elapsed millis.
/// Sits outside the handler so module code does not log directly per
/// AGENTS.md §4.27 (domain code does not log) — and so tenant/correlation
/// fields are populated from <see cref="IRequestContext"/> uniformly.
/// </summary>
public sealed class LoggingBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    private readonly ILogger<LoggingBehavior<TRequest, TResponse>> _logger;
    private readonly IRequestContext _requestContext;

    public LoggingBehavior(
        ILogger<LoggingBehavior<TRequest, TResponse>> logger,
        IRequestContext requestContext
    )
    {
        _logger = logger;
        _requestContext = requestContext;
    }

    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken
    )
    {
        var requestName = typeof(TRequest).Name;
        var stopwatch = Stopwatch.StartNew();

        try
        {
            var response = await next().ConfigureAwait(false);
            stopwatch.Stop();

            _logger.LogInformation(
                "Handled {RequestName} for tenant {TenantId} (correlation {CorrelationId}) in {ElapsedMs}ms",
                requestName,
                SafeTenantId(),
                _requestContext.CorrelationId,
                stopwatch.ElapsedMilliseconds
            );

            return response;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _logger.LogError(
                ex,
                "Failed {RequestName} for tenant {TenantId} (correlation {CorrelationId}) in {ElapsedMs}ms",
                requestName,
                SafeTenantId(),
                _requestContext.CorrelationId,
                stopwatch.ElapsedMilliseconds
            );
            throw;
        }
    }

    private string SafeTenantId()
    {
        try
        {
            return _requestContext.TenantId.ToString();
        }
        catch (InvalidOperationException)
        {
            return "(unset)";
        }
    }
}
