using Microsoft.EntityFrameworkCore;
using Npgsql;
using ShopFlow.Auth.Application.Ports;
using ShopFlow.Auth.Domain.Entities;
using ShopFlow.SharedKernel.Domain;

namespace ShopFlow.Auth.Infrastructure.Repositories;

/// <summary>
/// Sprint-9 U3 EF Core impl of <see cref="IRecoveryCodeRepository"/>.
/// Single-use is enforced via a raw SQL UPDATE with the
/// <c>used_at IS NULL</c> predicate inline — concurrent consumers race
/// on one row.
/// </summary>
public sealed class RecoveryCodeRepository : IRecoveryCodeRepository
{
    private const string UniqueViolationSqlState = "23505";

    private readonly AuthDbContext _db;

    public RecoveryCodeRepository(AuthDbContext db)
    {
        _db = db;
    }

    public async Task<Result> AddBatchAsync(Guid userId, IReadOnlyList<string> phcHashes, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(phcHashes);
        var now = DateTime.UtcNow;
        foreach (var hash in phcHashes)
        {
            await _db
                .RecoveryCodes
                .AddAsync(RecoveryCode.Issue(userId, hash, now), ct)
                .ConfigureAwait(false);
        }
        try
        {
            await _db.SaveChangesAsync(ct).ConfigureAwait(false);
            return Result.Success();
        }
        catch (DbUpdateException ex)
            when (ex.InnerException is PostgresException pg && pg.SqlState == UniqueViolationSqlState)
        {
            return Result.Failure("Recovery codes collide with existing rows.", "auth.recovery_codes_in_use");
        }
    }

    public async Task<bool> TryConsumeAsync(
        Guid userId,
        string plaintext,
        IPasswordHasher hasher,
        CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(plaintext);
        ArgumentNullException.ThrowIfNull(hasher);

        var candidates = await _db
            .RecoveryCodes
            .Where(c => c.UserId == userId && c.UsedAt == null)
            .Select(c => c.CodeHash)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        foreach (var hash in candidates)
        {
            if (!hasher.Verify(plaintext, hash))
            {
                continue;
            }

            // Race-safe consume: predicate-in-UPDATE so two concurrent
            // verifies for the same hash converge — exactly one returns
            // a row affected.
            var conn = _db.Database.GetDbConnection();
            if (conn.State != System.Data.ConnectionState.Open)
            {
                await conn.OpenAsync(ct).ConfigureAwait(false);
            }
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                UPDATE user_recovery_codes
                   SET used_at = @now
                 WHERE user_id = @user_id
                   AND code_hash = @code_hash
                   AND used_at IS NULL;
                """;
            var userParam = cmd.CreateParameter();
            userParam.ParameterName = "@user_id";
            userParam.Value = userId;
            cmd.Parameters.Add(userParam);
            var hashParam = cmd.CreateParameter();
            hashParam.ParameterName = "@code_hash";
            hashParam.Value = hash;
            cmd.Parameters.Add(hashParam);
            var nowParam = cmd.CreateParameter();
            nowParam.ParameterName = "@now";
            nowParam.Value = DateTime.UtcNow;
            cmd.Parameters.Add(nowParam);

            var affected = await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            return affected > 0;
        }

        return false;
    }

    public async Task<int> CountRemainingAsync(Guid userId, CancellationToken ct)
    {
        return await _db
            .RecoveryCodes
            .Where(c => c.UserId == userId && c.UsedAt == null)
            .CountAsync(ct)
            .ConfigureAwait(false);
    }

    public async Task DeleteAllAsync(Guid userId, CancellationToken ct)
    {
        await _db
            .RecoveryCodes
            .Where(c => c.UserId == userId)
            .ExecuteDeleteAsync(ct)
            .ConfigureAwait(false);
    }
}
