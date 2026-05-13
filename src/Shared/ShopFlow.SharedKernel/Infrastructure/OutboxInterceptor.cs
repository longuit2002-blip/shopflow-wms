using System.Diagnostics;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using ShopFlow.SharedKernel.Application;
using ShopFlow.SharedKernel.Domain;

namespace ShopFlow.SharedKernel.Infrastructure;

/// <summary>
/// EF Core interceptor implementing the outbox pattern from Tech Design
/// v3.0 §5.2: during <see cref="SavingChangesAsync"/> it harvests every
/// domain event raised on tracked <see cref="BaseEntity"/> instances,
/// writes a row to <c>outbox_messages</c> in the same transaction, and
/// clears the event buffer on each entity. Atomic with the business write —
/// if the write rolls back, no ghost outbox row.
/// </summary>
/// <remarks>
/// <para>
/// The dispatcher (<see cref="MultiplexedOutboxDispatcher{TContext}"/>) is
/// the consumer; this interceptor never publishes directly. AGENTS.md §6.38
/// forbids module code from calling <c>IPublishEndpoint.Publish</c> during
/// a write; the canonical path is "raise domain event → outbox row →
/// dispatcher".
/// </para>
/// <para>
/// Per ADR-0003 the v2.0 cross-tenant guard ("refuse to write outbox rows
/// stamped with a different tenant_id than IRequestContext") is removed —
/// the database identity is the tenant boundary, and there is no cross-
/// tenant write path to defend against at this layer. The interceptor
/// simply reads <c>IRequestContext.TenantId</c> and stamps it on every row
/// it emits.
/// </para>
/// </remarks>
public sealed class OutboxInterceptor : SaveChangesInterceptor
{
    private readonly IRequestContext _requestContext;

    public OutboxInterceptor(IRequestContext requestContext)
    {
        _requestContext = requestContext;
    }

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

    private void Capture(DbContext? context)
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
        var tenantId = _requestContext.TenantId;
        var outbox = context.Set<OutboxMessage>();

        foreach (var entity in entities)
        {
            foreach (var ev in entity.DomainEvents)
            {
                outbox.Add(
                    new OutboxMessage
                    {
                        Id = Guid.NewGuid(),
                        TenantId = tenantId,
                        EventType = ev.GetType().AssemblyQualifiedName!,
                        Payload = JsonSerializer.Serialize(ev, ev.GetType(), OutboxJsonOptions.Default),
                        TraceId = traceId,
                        CreatedAt = DateTime.UtcNow,
                    }
                );
            }

            entity.ClearDomainEvents();
        }
    }
}
