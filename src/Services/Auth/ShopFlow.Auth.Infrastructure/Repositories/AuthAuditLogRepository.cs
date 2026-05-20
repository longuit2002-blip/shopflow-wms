using ShopFlow.Auth.Application.Ports;
using ShopFlow.Auth.Domain.Entities;

namespace ShopFlow.Auth.Infrastructure.Repositories;

/// <summary>
/// Sprint-9 U3 EF Core impl of <see cref="IAuthAuditLogRepository"/>.
/// Append-only — never updates or deletes; the table is unpartitioned
/// in Sprint-9 (partitioning + archival are Sprint-10+ Scope Boundary).
/// </summary>
public sealed class AuthAuditLogRepository : IAuthAuditLogRepository
{
    private readonly AuthDbContext _db;

    public AuthAuditLogRepository(AuthDbContext db)
    {
        _db = db;
    }

    public async Task AppendAsync(
        string eventType,
        Guid? userId,
        string sourceIp,
        string userAgent,
        string metadataJson,
        Guid correlationId,
        CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(eventType);
        var entry = AuthAuditLogEntry.Record(
            eventType,
            userId,
            sourceIp,
            userAgent,
            metadataJson,
            correlationId,
            DateTime.UtcNow);
        await _db.AuthAuditLog.AddAsync(entry, ct).ConfigureAwait(false);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
    }
}
