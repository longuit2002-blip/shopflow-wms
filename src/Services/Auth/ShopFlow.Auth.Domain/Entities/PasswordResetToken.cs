namespace ShopFlow.Auth.Domain.Entities;

/// <summary>
/// Sprint-9 U3 — durable record of an outstanding password-reset
/// request. Only the SHA-256 hash of the plaintext token is persisted;
/// the plaintext is sent in the reset email + destroyed in-process.
/// </summary>
/// <remarks>
/// Single-use is enforced at the repository layer via predicate-in-UPDATE
/// (<c>WHERE token_hash = @h AND used_at IS NULL AND expires_at &gt; now</c>),
/// not on the aggregate — the UPDATE atomicity is the load-bearing
/// part, not aggregate-level invariants. The aggregate exposes
/// <see cref="IsExpired"/> + <see cref="IsConsumed"/> for read paths
/// and that's all.
/// </remarks>
public sealed class PasswordResetToken
{
    public byte[] TokenHash { get; private set; } = default!;

    public Guid UserId { get; private set; }

    public DateTime ExpiresAt { get; private set; }

    public DateTime CreatedAt { get; private set; }

    public DateTime? UsedAt { get; private set; }

    private PasswordResetToken() { }

    public static PasswordResetToken Issue(
        byte[] tokenHash,
        Guid userId,
        DateTime expiresAt,
        DateTime now
    )
    {
        ArgumentNullException.ThrowIfNull(tokenHash);
        if (tokenHash.Length == 0)
        {
            throw new ArgumentException("Token hash is required.", nameof(tokenHash));
        }
        if (expiresAt <= now)
        {
            throw new ArgumentException("ExpiresAt must be in the future.", nameof(expiresAt));
        }

        return new PasswordResetToken
        {
            TokenHash = tokenHash,
            UserId = userId,
            ExpiresAt = expiresAt,
            CreatedAt = now,
        };
    }

    public bool IsExpired(DateTime now) => ExpiresAt <= now;

    public bool IsConsumed => UsedAt is not null;
}
