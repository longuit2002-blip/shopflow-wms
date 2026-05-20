namespace ShopFlow.Auth.Domain.Entities;

/// <summary>
/// Sprint-9 U3 — per-user TOTP enrollment row. Stores the
/// AES-256-GCM-encrypted shared secret + the KEK key id used for that
/// encryption (KTD8 lazy rotation read path). One row per enrolled user.
/// </summary>
/// <remarks>
/// <see cref="LastUsedTimeStep"/> is the bookkeeping that prevents
/// within-window OTP replay. Each successful TOTP verify writes the
/// matched step; a subsequent verify rejects when the presented code
/// resolves to the same step. The handler in U8 is responsible for
/// reading + comparing + writing — the entity just persists the field.
/// </remarks>
public sealed class TotpSecret
{
    public Guid UserId { get; private set; }

    public byte[] EncryptedSecret { get; private set; } = default!;

    public int TotpKeyId { get; private set; }

    public long? LastUsedTimeStep { get; private set; }

    public DateTime CreatedAt { get; private set; }

    public DateTime? UpdatedAt { get; private set; }

    private TotpSecret() { }

    public static TotpSecret Create(Guid userId, byte[] encryptedSecret, int totpKeyId, DateTime now)
    {
        ArgumentNullException.ThrowIfNull(encryptedSecret);
        if (encryptedSecret.Length == 0)
        {
            throw new ArgumentException("Encrypted secret blob is required.", nameof(encryptedSecret));
        }
        return new TotpSecret
        {
            UserId = userId,
            EncryptedSecret = encryptedSecret,
            TotpKeyId = totpKeyId,
            CreatedAt = now,
        };
    }

    public void RecordVerifiedStep(long timeStep, DateTime now)
    {
        LastUsedTimeStep = timeStep;
        UpdatedAt = now;
    }

    public void Replace(byte[] encryptedSecret, int totpKeyId, DateTime now)
    {
        ArgumentNullException.ThrowIfNull(encryptedSecret);
        if (encryptedSecret.Length == 0)
        {
            throw new ArgumentException("Encrypted secret blob is required.", nameof(encryptedSecret));
        }
        EncryptedSecret = encryptedSecret;
        TotpKeyId = totpKeyId;
        LastUsedTimeStep = null;
        UpdatedAt = now;
    }
}
