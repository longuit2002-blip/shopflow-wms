using Microsoft.EntityFrameworkCore;
using Npgsql;
using ShopFlow.Auth.Application.Ports;
using ShopFlow.Auth.Domain;
using ShopFlow.Auth.Domain.Entities;
using ShopFlow.SharedKernel.Domain;

namespace ShopFlow.Auth.Infrastructure.Repositories;

/// <summary>
/// EF Core implementation of <see cref="IUserRepository"/> — Sprint-8 U3.
/// The DbContext is tenant-bound via <c>IRequestContext.DbConnectionString</c>
/// (AGENTS.md §3.17); the repo never sees a <c>tenantId</c> argument.
/// </summary>
/// <remarks>
/// <para>Email lookups go through the <c>ux_users_email_lower</c>
/// expression-index — <see cref="GetByEmailAsync"/> normalises the input
/// to lowercase to match the index expression. The aggregate factory
/// also normalises on insert, so case-collision races resolve to the
/// same row.</para>
///
/// <para><see cref="AddAsync"/> catches the Postgres <c>23505</c>
/// UNIQUE-violation and returns a <c>Result&lt;User&gt;</c> failure
/// tagged <c>auth.email_in_use</c> instead of unwinding the request as
/// an exception. Other 23505 sources can't reach this code path — the
/// <c>users</c> table has only the email expression-index as its
/// UNIQUE-bearing constraint (the PK violation would surface as a
/// pre-insert duplicate-Guid which is effectively impossible).</para>
/// </remarks>
public sealed class UserRepository : IUserRepository
{
    private readonly AuthDbContext _db;

    public UserRepository(AuthDbContext db)
    {
        _db = db;
    }

    public async Task<User?> GetByEmailAsync(string email, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(email);
        var normalized = email.Trim().ToLowerInvariant();
        return await _db
            .Users.AsTracking()
            .FirstOrDefaultAsync(u => u.Email == normalized, ct)
            .ConfigureAwait(false);
    }

    public async Task<User?> GetByIdAsync(Guid userId, CancellationToken ct)
    {
        return await _db
            .Users.AsTracking()
            .FirstOrDefaultAsync(u => u.Id == userId, ct)
            .ConfigureAwait(false);
    }

    public async Task<Result<User>> AddAsync(User user, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(user);

        await _db.Users.AddAsync(user, ct).ConfigureAwait(false);
        try
        {
            await _db.SaveChangesAsync(ct).ConfigureAwait(false);
            return Result<User>.Success(user);
        }
        catch (DbUpdateException ex)
            when (ex.InnerException is PostgresException pg
                && pg.SqlState == PostgresErrorCodes.UniqueViolation
            )
        {
            // Detach the conflicting entity so the DbContext stays
            // usable for the caller's follow-up path (e.g., the admin
            // handler may want to look up the existing row to produce
            // a richer 409 body).
            _db.Entry(user).State = EntityState.Detached;
            return Result<User>.Failure(
                $"A user with email '{user.Email}' already exists in this tenant.",
                "auth.email_in_use"
            );
        }
    }

    public async Task UpdateAsync(User user, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(user);
        // The aggregate is already tracked from a prior GetByEmail /
        // GetById; SaveChanges flushes the named-method mutations
        // (UpdatePassword / SetRole / Deactivate / RecordLogin) plus
        // drains the domain-event buffer via the OutboxInterceptor.
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<User>> ListAsync(int page, int pageSize, CancellationToken ct)
    {
        if (page < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(page), "page is 1-based.");
        }
        if (pageSize < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(pageSize), "pageSize must be positive.");
        }
        return await _db
            .Users.AsNoTracking()
            .OrderByDescending(u => u.CreatedAt)
            .ThenByDescending(u => u.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct)
            .ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<User>> ListByRoleAsync(UserRole role, CancellationToken ct)
    {
        // Sprint-9 U2: full scan by role. Owner row count is typically
        // 1-3 per tenant so paging is unnecessary; non-Owner callers
        // should use ListAsync with explicit paging instead. AsTracking
        // is intentional — Sprint-9 Notification consumers don't mutate
        // the row but other call sites may (e.g. fan-out audit emit).
        return await _db
            .Users.AsNoTracking()
            .Where(u => u.Role == role && u.IsActive)
            .OrderBy(u => u.CreatedAt)
            .ToListAsync(ct)
            .ConfigureAwait(false);
    }
}
