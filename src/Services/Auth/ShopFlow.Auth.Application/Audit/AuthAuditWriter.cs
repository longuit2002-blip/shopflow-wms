using System.Text.Json;
using Microsoft.Extensions.Logging;
using ShopFlow.Auth.Application.Ports;
using ShopFlow.SharedKernel.Infrastructure;

namespace ShopFlow.Auth.Application.Audit;

/// <summary>
/// Sprint-12.5 U1 — handler-side fire-and-forget wrapper around
/// <see cref="IAuthAuditLogRepository.AppendAsync"/>. Centralises the
/// canonical try/catch + Warning-log pattern so handlers don't repeat
/// the same boilerplate at every terminal emit.
/// </summary>
/// <remarks>
/// <para><b>Best-effort with exception suppression, NOT latency
/// isolation.</b> The handler still awaits the underlying
/// <c>AppendAsync</c>; a slow Postgres on the tenant DB will increase
/// the calling auth path's response p99. If production traces show
/// audit latency contaminating login p99, Sprint-13+ may evolve to a
/// background-channel dispatch. See
/// <c>docs/solutions/2026-05-26-fire-and-forget-audit-write-pattern.md</c>.</para>
///
/// <para>Source-IP correctness depends on the SharedKernel
/// <c>ForwardedHeaders</c> middleware (Sprint-9 KTD7). Auth.Api's
/// <c>UseShopFlowSecurityPipeline()</c> wires it; the non-Development
/// boot guard requires <c>KnownProxies + KnownNetworks</c>.</para>
/// </remarks>
public static class AuthAuditWriter
{
    /// <summary>
    /// Append a single audit row. Swallows any exception thrown by the
    /// repository, logs at Warning, and never propagates to the caller.
    /// </summary>
    /// <param name="auditLog">The Sprint-9 repository port.</param>
    /// <param name="logger">Handler's logger for the Warning entry.</param>
    /// <param name="eventType">One of <see cref="AuthAuditEventTypes"/>.</param>
    /// <param name="userId">Resolved user id; null for failed-login on unknown email.</param>
    /// <param name="sourceIp">Client IP from <c>HttpContext.Connection.RemoteIpAddress</c>.</param>
    /// <param name="userAgent">Raw <c>User-Agent</c> header value.</param>
    /// <param name="metadata">Per-event-type structured payload; serialized via
    /// <see cref="OutboxJsonOptions.Default"/> (camelCase).</param>
    /// <param name="correlationId">Per-request W3C TraceContext id.</param>
    /// <param name="ct">Cancellation token.</param>
    public static async Task TryAppendAsync(
        IAuthAuditLogRepository auditLog,
        ILogger logger,
        string eventType,
        Guid? userId,
        string sourceIp,
        string userAgent,
        object? metadata,
        Guid correlationId,
        CancellationToken ct
    )
    {
        ArgumentNullException.ThrowIfNull(auditLog);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(eventType);

        var metadataJson = metadata is null
            ? "{}"
            : JsonSerializer.Serialize(metadata, OutboxJsonOptions.Default);

        try
        {
            await auditLog
                .AppendAsync(
                    eventType,
                    userId,
                    sourceIp ?? "unknown",
                    userAgent ?? string.Empty,
                    metadataJson,
                    correlationId,
                    ct
                )
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // R2 — never propagate. Warning log so production traces
            // catch the audit-table outage without contaminating the
            // auth response.
            logger.LogWarning(
                ex,
                "Audit write failed for {EventType} (userId={UserId}, correlationId={CorrelationId})",
                eventType,
                userId,
                correlationId
            );
        }
    }
}
