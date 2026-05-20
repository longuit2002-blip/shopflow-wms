using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;
using ShopFlow.Auth.Application.Services;
using ShopFlow.Auth.Infrastructure.Hashing;

namespace ShopFlow.Migrate.Provisioning;

/// <summary>
/// Sprint-8 U10 — seeds the first Owner row into a per-tenant
/// <c>users</c> table after the AddUsers migration has applied.
/// Delegates hashing + password-generation to Auth.Infrastructure so
/// the seed user's PHC hash validates 1:1 against
/// <c>Argon2idPasswordHasher.Verify</c> at login time (SG-001 — no
/// duplicate hashing logic).
/// </summary>
public sealed class OwnerSeed
{
    private readonly IPasswordGenerator _generator;
    private readonly ILogger<OwnerSeed> _logger;

    public OwnerSeed(IPasswordGenerator generator, ILogger<OwnerSeed> logger)
    {
        _generator = generator;
        _logger = logger;
    }

    /// <summary>
    /// Insert one Owner row into the tenant DB at
    /// <paramref name="tenantConnectionString"/>. Idempotent on email
    /// — if a row with the same lowercase email already exists,
    /// returns <see cref="OwnerSeedOutcome.AlreadySeeded"/> without
    /// duplicating.
    /// </summary>
    /// <param name="ownerEmail">Owner email. Caller's responsibility
    /// to ensure it conforms to the User aggregate's regex; the DB
    /// CHECK constraint catches malformed cases.</param>
    /// <param name="explicitPassword">If non-null, hash + use this
    /// instead of generating one. Used by CI flows that pass
    /// credentials in via env so the generated plaintext doesn't echo
    /// to job logs.</param>
    public async Task<OwnerSeedResult> SeedAsync(
        string tenantConnectionString,
        string ownerEmail,
        string? explicitPassword,
        CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantConnectionString);
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerEmail);

        var normalizedEmail = ownerEmail.Trim().ToLowerInvariant();

        await using var conn = new NpgsqlConnection(tenantConnectionString);
        await conn.OpenAsync(ct).ConfigureAwait(false);

        // Idempotency check: same email already seeded → no-op.
        await using (var exists = conn.CreateCommand())
        {
            exists.CommandText = "SELECT COUNT(*) FROM users WHERE lower(email) = @e;";
            exists.Parameters.AddWithValue("e", normalizedEmail);
            var count = Convert.ToInt64(await exists.ExecuteScalarAsync(ct).ConfigureAwait(false));
            if (count > 0)
            {
                _logger.LogInformation(
                    "Owner '{Email}' already exists in tenant DB; seed is a no-op.", normalizedEmail);
                return new OwnerSeedResult(OwnerSeedOutcome.AlreadySeeded, normalizedEmail, null);
            }
        }

        var passwordWasGenerated = string.IsNullOrEmpty(explicitPassword);
        var plaintext = passwordWasGenerated ? _generator.Generate() : explicitPassword!;
        var hasher = BuildHasher();
        var hash = hasher.Hash(plaintext);

        await using (var insert = conn.CreateCommand())
        {
            // Sprint-9 U12 — Owner rows ship with mfa_required = true so
            // the first login triggers the forced enrollment flow per
            // R17. The Domain factory User.Create defaults this same
            // way for application-level creates; the raw-SQL seed path
            // mirrors that invariant.
            insert.CommandText =
                "INSERT INTO users (id, email, password_hash, role, is_active, created_at, "
                + "failed_login_count, mfa_required, mfa_enrolled) "
                + "VALUES (@id, @email, @hash, 'Owner', true, NOW(), 0, true, false);";
            insert.Parameters.AddWithValue("id", Guid.NewGuid());
            insert.Parameters.AddWithValue("email", normalizedEmail);
            insert.Parameters.AddWithValue("hash", hash);
            await insert.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }

        return new OwnerSeedResult(
            OwnerSeedOutcome.Seeded,
            normalizedEmail,
            passwordWasGenerated ? plaintext : null);
    }

    private static Argon2idPasswordHasher BuildHasher() =>
        new(Options.Create(new Argon2Options()));
}

/// <summary>
/// Outcome of <see cref="OwnerSeed.SeedAsync"/>. <see cref="GeneratedPassword"/>
/// is non-null only when the seed generated the password (caller
/// echoes it to stdout once); when the caller supplied
/// <c>--owner-password</c>, this is null so the CLI doesn't re-echo a
/// user-supplied secret.
/// </summary>
public sealed record OwnerSeedResult(
    OwnerSeedOutcome Outcome,
    string OwnerEmail,
    string? GeneratedPassword);

public enum OwnerSeedOutcome
{
    Seeded,
    AlreadySeeded,
}
