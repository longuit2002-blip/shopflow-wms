using System.Diagnostics;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using ShopFlow.SharedKernel.Domain;

namespace ShopFlow.SharedKernel.Infrastructure;

/// <summary>
/// EF Core interceptor implementing the outbox pattern from Tech Design §11.2:
/// during <see cref="SavingChangesAsync"/> it harvests every domain event
/// raised on tracked <see cref="BaseEntity"/> instances, writes a row to
/// <c>outbox_messages</c> in the same transaction, and clears the event
/// buffer on each entity. Atomic with the business write — if the write
/// rolls back, no ghost outbox row.
/// </summary>
/// <remarks>
/// The dispatcher (<see cref="OutboxDispatcher"/>) is the consumer; this
/// interceptor never publishes directly. AGENTS.md §6.35 forbids module
/// code from calling <c>IPublishEndpoint.Publish</c> during a write; the
/// canonical path is "raise domain event → outbox row → dispatcher".
/// </remarks>
public sealed class OutboxInterceptor : SaveChangesInterceptor
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = false,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public override InterceptionResult<int> SavingChanges(
        DbContextEventData eventData,
        InterceptionResult<int> result
    )
    {
        Capture(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default
    )
    {
        Capture(eventData.Context);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    private static void Capture(DbContext? context)
    {
        if (context is null)
        {
            return;
        }

        var entities = context
            .ChangeTracker.Entries<BaseEntity>()
            .Select(e => e.Entity)
            .Where(e => e.DomainEvents.Count > 0)
            .ToList();

        if (entities.Count == 0)
        {
            return;
        }

        var traceId = Activity.Current?.TraceId.ToString();
        var outbox = context.Set<OutboxMessage>();

        foreach (var entity in entities)
        {
            foreach (var ev in entity.DomainEvents)
            {
                outbox.Add(
                    new OutboxMessage
                    {
                        Id = Guid.NewGuid(),
                        TenantId = ev.TenantId,
                        EventType = ev.GetType().AssemblyQualifiedName!,
                        Payload = JsonSerializer.Serialize(ev, ev.GetType(), SerializerOptions),
                        TraceId = traceId,
                        CreatedAt = DateTime.UtcNow,
                    }
                );
            }

            entity.ClearDomainEvents();
        }
    }
}
