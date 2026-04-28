using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;
using ShopFlow.SharedKernel.Application;
using ShopFlow.SharedKernel.Domain;

namespace ShopFlow.SharedKernel.Infrastructure;

/// <summary>
/// EF Core interceptor that enforces tenant scoping at write time:
///   1. Stamps <c>TenantId</c> on every Added <see cref="BaseEntity"/> from
///      the ambient <see cref="IRequestContext"/>.
///   2. Refuses writes for entities whose <c>TenantId</c> does not match
///      the active request — the cross-tenant write bug is converted into
///      a loud exception at the persistence boundary instead of a silent
///      data leak (AGENTS.md §3.17, §3.18).
///
/// Read-side scoping is handled by Postgres RLS plus EF Core global query
/// filters (configured in each module's DbContext); this interceptor only
/// guards the write path.
/// </summary>
public sealed class TenancyInterceptor : SaveChangesInterceptor
{
    private readonly IRequestContext _requestContext;

    public TenancyInterceptor(IRequestContext requestContext)
    {
        _requestContext = requestContext;
    }

    public override InterceptionResult<int> SavingChanges(
        DbContextEventData eventData,
        InterceptionResult<int> result
    )
    {
        Apply(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default
    )
    {
        Apply(eventData.Context);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    private void Apply(DbContext? context)
    {
        if (context is null)
        {
            return;
        }

        // TenantId access throws if the request boundary failed to populate
        // the context. That's the desired loud failure — better than the
        // silent zero-tenant write that any falsy default would produce.
        var tenantId = _requestContext.TenantId;

        foreach (EntityEntry entry in context.ChangeTracker.Entries())
        {
            if (entry.Entity is not BaseEntity entity)
            {
                continue;
            }

            switch (entry.State)
            {
                case EntityState.Added:
                    if (entity.TenantId == Guid.Empty)
                    {
                        // Reflection write because TenantId has a protected setter on BaseEntity.
                        entry.Property(nameof(BaseEntity.TenantId)).CurrentValue = tenantId;
                    }
                    else if (entity.TenantId != tenantId)
                    {
                        throw new InvalidOperationException(
                            $"Cross-tenant write blocked: entity of type {entity.GetType().Name} "
                                + $"carries TenantId={entity.TenantId} but the request is scoped to {tenantId}."
                        );
                    }
                    break;

                case EntityState.Modified:
                case EntityState.Deleted:
                    if (entity.TenantId != tenantId)
                    {
                        throw new InvalidOperationException(
                            $"Cross-tenant {entry.State.ToString().ToLowerInvariant()} blocked: entity of type "
                                + $"{entity.GetType().Name} carries TenantId={entity.TenantId} but the request is scoped to {tenantId}."
                        );
                    }
                    break;
            }
        }
    }
}
