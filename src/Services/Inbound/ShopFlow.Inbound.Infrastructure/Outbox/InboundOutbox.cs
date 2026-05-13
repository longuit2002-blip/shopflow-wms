using System.Diagnostics;
using System.Text.Json;
using ShopFlow.Inbound.Application.Ports;
using ShopFlow.SharedKernel.Application;
using ShopFlow.SharedKernel.Infrastructure;

namespace ShopFlow.Inbound.Infrastructure.Outbox;

/// <summary>
/// EF Core-backed implementation of <see cref="IInboundOutbox"/>. Stamps
/// <c>tenant_id</c> from the ambient <see cref="IRequestContext"/> and
/// the <c>trace_id</c> from <see cref="Activity.Current"/> at enqueue
/// time. The row participates in whatever transaction the caller's
/// <c>SaveChangesAsync</c> commits — atomic with the business write.
/// </summary>
public sealed class InboundOutbox : IInboundOutbox
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = false,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly InboundDbContext _db;
    private readonly IRequestContext _requestContext;

    public InboundOutbox(InboundDbContext db, IRequestContext requestContext)
    {
        _db = db;
        _requestContext = requestContext;
    }

    public void Enqueue<T>(T integrationEvent, DateTime occurredAt)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);
        var traceId = Activity.Current?.TraceId.ToString();
        _db.OutboxMessages.Add(
            new OutboxMessage
            {
                Id = Guid.NewGuid(),
                TenantId = _requestContext.TenantId,
                EventType = typeof(T).AssemblyQualifiedName!,
                Payload = JsonSerializer.Serialize(integrationEvent, typeof(T), JsonOptions),
                TraceId = traceId,
                CreatedAt = occurredAt,
            }
        );
    }
}
