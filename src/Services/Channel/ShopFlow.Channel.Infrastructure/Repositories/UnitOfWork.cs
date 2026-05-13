using ShopFlow.Channel.Application.Ports;

namespace ShopFlow.Channel.Infrastructure.Repositories;

/// <summary>
/// EF Core-backed <see cref="IUnitOfWork"/> per Sprint-4 plan U3. Wraps the
/// per-request <see cref="ChannelDbContext"/> SaveChanges call. The
/// orchestrator owns the transaction boundary; this type carries no
/// transaction state of its own.
/// </summary>
public sealed class UnitOfWork : IUnitOfWork
{
    private readonly ChannelDbContext _db;

    public UnitOfWork(ChannelDbContext db)
    {
        _db = db;
    }

    public Task<int> SaveChangesAsync(CancellationToken ct) => _db.SaveChangesAsync(ct);
}
