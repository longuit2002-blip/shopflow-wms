namespace ShopFlow.Auth.Application.Ports;

/// <summary>
/// Per-tenant outbox-append port for the Auth module (Sprint-9 U9
/// ships the impl). Mirrors the
/// <c>IOutboundOutbox</c> + <c>IInboundOutbox</c> shape from
/// Sprint-2-redux / Sprint-3-redux: the handler writes the cross-module
/// event into <c>auth_outbox_messages</c> in the same tracked-DbContext
/// save as the business write, the
/// <c>MultiplexedOutboxDispatcher&lt;AuthDbContext&gt;</c> hosted
/// service polls and publishes to RabbitMQ.
/// </summary>
/// <remarks>
/// Sprint-9 emits four contracts via this port:
/// <c>PasswordResetRequestedV1</c>, <c>RefreshReuseDetectedV1</c>,
/// <c>AccountLockedV1</c>, <c>MfaEnrolledV1</c> — each registered as
/// <c>AddOutboxRoute&lt;T&gt;(SendKind.Publish)</c> in U9.
/// </remarks>
public interface IAuthOutbox
{
    /// <summary>
    /// Append a single event row. <paramref name="eventType"/> is the
    /// CLR type's full name (the outbox dispatcher reads this to
    /// resolve the registered route). <paramref name="payload"/> is
    /// serialized via <c>OutboxJsonOptions.Default</c> (Sprint-2.5
    /// camelCase / case-insensitive).
    /// </summary>
    Task AppendAsync(string eventType, object payload, CancellationToken ct);
}
