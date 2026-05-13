using ShopFlow.Outbound.Application.Ports;

namespace ShopFlow.Outbound.Infrastructure.Repositories;

/// <summary>
/// One-line wrapper that exposes <c>OutboundDbContext.SaveChangesAsync</c>
/// as <see cref="IUnitOfWork.SaveChangesAsync"/>. Lets handlers commit
/// without taking a direct dependency on EF Core (AGENTS.md §3.16) —
/// same pattern as Sprint-2-redux's <c>InboundUnitOfWork</c>.
/// </summary>
public sealed class OutboundUnitOfWork : IUnitOfWork
{
    private readonly OutboundDbContext _db;

    public OutboundUnitOfWork(OutboundDbContext db)
    {
        _db = db;
    }

    public Task<int> SaveChangesAsync(CancellationToken ct) => _db.SaveChangesAsync(ct);
}
