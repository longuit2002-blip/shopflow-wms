using System.Diagnostics;
using System.Text.Json;
using ShopFlow.Auth.Application.Ports;
using ShopFlow.SharedKernel.Application;
using ShopFlow.SharedKernel.Infrastructure;

namespace ShopFlow.Auth.Infrastructure.Outbox;

/// <summary>
/// Sprint-9 U9 EF Core-backed <see cref="IAuthOutbox"/>. Mirrors the
/// Sprint-2-redux Inbound + Sprint-3-redux Outbound outbox shape. Writes
/// the cross-module event payload into <c>auth_outbox_messages</c> in
/// the same tracked-DbContext save as the business write — atomic with
/// the handler's other DB mutations.
/// </summary>
public sealed class AuthOutbox : IAuthOutbox
{
    private readonly AuthDbContext _db;
    private readonly IRequestContext _requestContext;
    private readonly TimeProvider _clock;

    public AuthOutbox(AuthDbContext db, IRequestContext requestContext, TimeProvider clock)
    {
        _db = db;
        _requestContext = requestContext;
        _clock = clock;
    }

    public async Task AppendAsync(string eventType, object payload, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(eventType);
        ArgumentNullException.ThrowIfNull(payload);

        var traceId = Activity.Current?.TraceId.ToString();
        var json = JsonSerializer.Serialize(payload, payload.GetType(), OutboxJsonOptions.Default);

        await _db
            .OutboxMessages.AddAsync(
                new OutboxMessage
                {
                    Id = Guid.NewGuid(),
                    TenantId = _requestContext.TenantId,
                    EventType = eventType,
                    Payload = json,
                    TraceId = traceId,
                    CreatedAt = _clock.GetUtcNow().UtcDateTime,
                },
                ct)
            .ConfigureAwait(false);
    }
}
