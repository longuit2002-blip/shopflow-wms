using ShopFlow.Inbound.Application.Ports;

namespace ShopFlow.Inbound.Infrastructure.Repositories;

/// <summary>
/// One-line wrapper that exposes <see cref="InboundDbContext.SaveChangesAsync"/>
/// as <see cref="IUnitOfWork.SaveChangesAsync"/>. Lets handlers commit
/// without taking a direct dependency on EF Core (AGENTS.md §3.16).
/// </summary>
public sealed class InboundUnitOfWork : IUnitOfWork
{
    private readonly InboundDbContext _db;

    public InboundUnitOfWork(InboundDbContext db)
    {
        _db = db;
    }

    public Task<int> SaveChangesAsync(CancellationToken ct) => _db.SaveChangesAsync(ct);
}
