using System.Diagnostics;
using System.Text.Json;
using ShopFlow.Outbound.Application.Ports;
using ShopFlow.SharedKernel.Application;
using ShopFlow.SharedKernel.Infrastructure;

namespace ShopFlow.Outbound.Infrastructure.Outbox;

/// <summary>
/// EF Core-backed implementation of <see cref="IOutboundOutbox"/>. Stamps
/// <c>tenant_id</c> from the ambient <see cref="IRequestContext"/> and
/// the <c>trace_id</c> from <see cref="Activity.Current"/> at enqueue
/// time. The row participates in whatever transaction the caller's
/// <c>SaveChangesAsync</c> commits — atomic with the business write.
/// </summary>
/// <remarks>
/// Serializes with <see cref="OutboxJsonOptions.Default"/> per the
/// Sprint-2.5 standard (camelCase serialise, case-insensitive
/// deserialise on the dispatcher side). Direct mirror of
/// <c>ShopFlow.Inbound.Infrastructure.Outbox.InboundOutbox</c>; the only
/// difference is the <see cref="OutboundDbContext"/> binding so the row
/// lands in <c>outbound_outbox_messages</c> per the Sprint-2.5 prefix
/// convention.
/// </remarks>
public sealed class OutboundOutbox : IOutboundOutbox
{
    private readonly OutboundDbContext _db;
    private readonly IRequestContext _requestContext;
    private readonly TimeProvider _clock;

    public OutboundOutbox(OutboundDbContext db, IRequestContext requestContext, TimeProvider clock)
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
                ct
            )
            .ConfigureAwait(false);
    }
}
