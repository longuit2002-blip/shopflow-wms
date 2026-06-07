using Microsoft.EntityFrameworkCore;
using ShopFlow.Auth.Application.Ports;
using ShopFlow.Auth.Domain.Entities;

namespace ShopFlow.Auth.Infrastructure.Repositories;

/// <summary>
/// Sprint-9 U3 EF Core impl of <see cref="ITotpSecretRepository"/>. The
/// row is rewritten on every TOTP step bump; concurrent OTP verifies
/// for the same user are extremely rare (a user has one authenticator),
/// so the last-write-wins semantics are acceptable.
/// </summary>
public sealed class TotpSecretRepository : ITotpSecretRepository
{
    private readonly AuthDbContext _db;

    public TotpSecretRepository(AuthDbContext db)
    {
        _db = db;
    }

    public async Task<TotpSecretView?> GetAsync(Guid userId, CancellationToken ct)
    {
        var row = await _db
            .TotpSecrets.AsNoTracking()
            .FirstOrDefaultAsync(s => s.UserId == userId, ct)
            .ConfigureAwait(false);
        return row is null
            ? null
            : new TotpSecretView(row.EncryptedSecret, row.TotpKeyId, row.LastUsedTimeStep);
    }

    public async Task UpsertAsync(
        Guid userId,
        byte[] encryptedSecret,
        int keyId,
        long? lastUsedTimeStep,
        CancellationToken ct
    )
    {
        ArgumentNullException.ThrowIfNull(encryptedSecret);
        var existing = await _db
            .TotpSecrets.FirstOrDefaultAsync(s => s.UserId == userId, ct)
            .ConfigureAwait(false);
        if (existing is null)
        {
            var fresh = TotpSecret.Create(userId, encryptedSecret, keyId, DateTime.UtcNow);
            if (lastUsedTimeStep is not null)
            {
                fresh.RecordVerifiedStep(lastUsedTimeStep.Value, DateTime.UtcNow);
            }
            await _db.TotpSecrets.AddAsync(fresh, ct).ConfigureAwait(false);
        }
        else
        {
            existing.Replace(encryptedSecret, keyId, DateTime.UtcNow);
            if (lastUsedTimeStep is not null)
            {
                existing.RecordVerifiedStep(lastUsedTimeStep.Value, DateTime.UtcNow);
            }
        }
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    public async Task UpdateLastUsedStepAsync(Guid userId, long timeStep, CancellationToken ct)
    {
        var row = await _db
            .TotpSecrets.FirstOrDefaultAsync(s => s.UserId == userId, ct)
            .ConfigureAwait(false);
        if (row is null)
        {
            return;
        }
        row.RecordVerifiedStep(timeStep, DateTime.UtcNow);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    public async Task DeleteAsync(Guid userId, CancellationToken ct)
    {
        await _db
            .TotpSecrets.Where(s => s.UserId == userId)
            .ExecuteDeleteAsync(ct)
            .ConfigureAwait(false);
    }
}
