using System.Diagnostics;
using System.Text.Json;
using ShopFlow.Channel.Application.Ports;
using ShopFlow.SharedKernel.Application;
using ShopFlow.SharedKernel.Infrastructure;

namespace ShopFlow.Channel.Infrastructure.Outbox;

/// <summary>
/// EF Core-backed implementation of <see cref="IChannelOutbox"/> per Sprint-4
/// plan U3. Stamps <c>tenant_id</c> from the ambient
/// <see cref="IRequestContext"/> and the <c>trace_id</c> from
/// <see cref="Activity.Current"/> at enqueue time. The row participates in
/// whatever transaction the caller's <c>SaveChangesAsync</c> commits —
/// atomic with the <c>webhook_events</c> insert. Direct mirror of
/// Sprint-3-redux's <c>OutboundOutbox</c>; the only difference is the
/// <see cref="ChannelDbContext"/> binding so the row lands in
/// <c>channel_outbox_messages</c> per the Sprint-2.5 prefix convention.
/// </summary>
public sealed class ChannelOutbox : IChannelOutbox
{
    private readonly ChannelDbContext _db;
    private readonly IRequestContext _requestContext;
    private readonly TimeProvider _clock;

    public ChannelOutbox(ChannelDbContext db, IRequestContext requestContext, TimeProvider clock)
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
