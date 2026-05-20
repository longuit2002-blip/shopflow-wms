namespace ShopFlow.Auth.Application.Ports;

/// <summary>
/// Port for the AES-256-GCM envelope cipher protecting TOTP shared
/// secrets at rest (Sprint-9 U4 ships the impl). The cipher reads its
/// KEK material from <c>Auth:TotpKek:Current</c> + the rotation slot
/// <c>Auth:TotpKek:Previous</c> per KTD8.
/// </summary>
/// <remarks>
/// <para>The on-disk blob layout is <c>[nonce(12)][cipher(N)][tag(16)]</c>
/// concatenated; the <c>totp_key_id</c> column on
/// <c>user_totp_secrets</c> identifies which KEK to use on read. KEK
/// rotation is lazy: bump Current, set Previous = old Current, deploy;
/// readers fall back to Previous when the row's key id != Current. The
/// background re-encrypt sweep that drains Previous-encrypted rows to
/// Current is Sprint-10+ ops work (Scope Boundary).</para>
///
/// <para>Per-row AAD is <c>tenant_id || user_id</c> so a ciphertext
/// blob lifted out of one user's row cannot be replayed against
/// another user's enrollment row.</para>
/// </remarks>
public interface ITotpSecretCipher
{
    /// <summary>
    /// Encrypt with the Current KEK. Returns the framed blob and the
    /// key id that produced it (write into
    /// <c>user_totp_secrets.totp_key_id</c>).
    /// </summary>
    (byte[] CiphertextBlob, int KeyId) Encrypt(byte[] plaintext, Guid tenantId, Guid userId);

    /// <summary>
    /// Decrypt with the KEK identified by <paramref name="keyId"/>.
    /// Returns null when the key id is not <c>Current</c> or
    /// <c>Previous</c> (rotation completed without re-encrypt); the
    /// caller treats null as a hard auth failure and surfaces
    /// <c>auth.mfa_secret_unrecoverable</c> for ops alerting. Throws
    /// <c>System.Security.Cryptography.AuthenticationTagMismatchException</c>
    /// when the blob is tampered.
    /// </summary>
    byte[]? Decrypt(byte[] ciphertextBlob, int keyId, Guid tenantId, Guid userId);

    /// <summary>The Current key id — handlers write this with new ciphertexts.</summary>
    int CurrentKeyId { get; }
}
