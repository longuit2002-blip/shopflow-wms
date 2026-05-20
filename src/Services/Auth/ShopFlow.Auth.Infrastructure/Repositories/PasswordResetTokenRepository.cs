using Microsoft.EntityFrameworkCore;
using Npgsql;
using ShopFlow.Auth.Application.Ports;
using ShopFlow.Auth.Domain.Entities;
using ShopFlow.SharedKernel.Domain;

namespace ShopFlow.Auth.Infrastructure.Repositories;

/// <summary>
/// Sprint-9 U3 EF Core impl of <see cref="IPasswordResetTokenRepository"/>.
/// Wraps UNIQUE-23505 on <c>token_hash</c> → <c>auth.token_in_use</c>
/// Result mirroring Sprint-8 <c>UserRepository</c>. The single-use
/// consume runs as a raw SQL UPDATE with the predicate inline (KTD per
/// docs/solutions/2026-05-13-multi-row-cte-predicate-must-live-in-update.md).
/// </summary>
public sealed class PasswordResetTokenRepository : IPasswordResetTokenRepository
{
    private const string UniqueViolationSqlState = "23505";

    private readonly AuthDbContext _db;

    public PasswordResetTokenRepository(AuthDbContext db)
    {
        _db = db;
    }

    public async Task<Result> AddAsync(byte[] tokenHash, Guid userId, DateTime expiresAt, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(tokenHash);
        var token = PasswordResetToken.Issue(tokenHash, userId, expiresAt, DateTime.UtcNow);
        await _db.PasswordResetTokens.AddAsync(token, ct).ConfigureAwait(false);
        try
        {
            await _db.SaveChangesAsync(ct).ConfigureAwait(false);
            return Result.Success();
        }
        catch (DbUpdateException ex)
            when (ex.InnerException is PostgresException pg && pg.SqlState == UniqueViolationSqlState)
        {
            return Result.Failure("Password reset token already exists.", "auth.token_in_use");
        }
    }

    public async Task<Result<Guid>> TryConsumeAsync(byte[] tokenHash, TimeProvider clock, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(tokenHash);
        ArgumentNullException.ThrowIfNull(clock);
        var now = clock.GetUtcNow().UtcDateTime;

        // Predicate-in-UPDATE: consume only if the token row is active
        // (not yet used) and not expired. Concurrent consumers race on
        // a single row UPDATE — the loser sees 0 rows affected. Postgres
        // RETURNING clause gives us the user_id in the same round-trip.
        var conn = _db.Database.GetDbConnection();
        if (conn.State != System.Data.ConnectionState.Open)
        {
            await conn.OpenAsync(ct).ConfigureAwait(false);
        }
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            UPDATE password_reset_tokens
               SET used_at = @now
             WHERE token_hash = @token_hash
               AND used_at IS NULL
               AND expires_at > @now
            RETURNING user_id;
            """;
        var hashParam = cmd.CreateParameter();
        hashParam.ParameterName = "@token_hash";
        hashParam.Value = tokenHash;
        cmd.Parameters.Add(hashParam);
        var nowParam = cmd.CreateParameter();
        nowParam.ParameterName = "@now";
        nowParam.Value = now;
        cmd.Parameters.Add(nowParam);

        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        if (!await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            return Result<Guid>.Failure("Reset token not active.", "auth.invalid_token");
        }

        var userId = reader.GetGuid(0);
        return Result<Guid>.Success(userId);
    }

    public async Task<DateTime?> GetLastIssuedAtAsync(Guid userId, CancellationToken ct)
    {
        return await _db
            .PasswordResetTokens
            .AsNoTracking()
            .Where(t => t.UserId == userId)
            .OrderByDescending(t => t.CreatedAt)
            .Select(t => (DateTime?)t.CreatedAt)
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);
    }
}
