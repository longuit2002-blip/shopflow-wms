namespace ShopFlow.Auth.Domain.Entities;

/// <summary>
/// Sprint-9 U3 — per-user recovery code row. Composite PK
/// <c>(user_id, code_hash)</c>; <see cref="CodeHash"/> is the
/// Argon2id-RecoveryCode-profile PHC string. Each enrollment mints 10
/// codes; regenerate replaces the batch.
/// </summary>
public sealed class RecoveryCode
{
    public Guid UserId { get; private set; }

    public string CodeHash { get; private set; } = default!;

    public DateTime CreatedAt { get; private set; }

    public DateTime? UsedAt { get; private set; }

    private RecoveryCode() { }

    public static RecoveryCode Issue(Guid userId, string codeHash, DateTime now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(codeHash);
        return new RecoveryCode
        {
            UserId = userId,
            CodeHash = codeHash,
            CreatedAt = now,
        };
    }
}
