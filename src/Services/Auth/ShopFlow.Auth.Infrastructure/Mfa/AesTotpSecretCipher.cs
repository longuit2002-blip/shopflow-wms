using System.Security.Cryptography;
using Microsoft.Extensions.Options;
using ShopFlow.Auth.Application.Ports;

namespace ShopFlow.Auth.Infrastructure.Mfa;

/// <summary>
/// Sprint-9 U4 AES-256-GCM envelope cipher for TOTP shared secrets at
/// rest (KTD8). On-disk blob layout is <c>[nonce(12)][cipher(N)][tag(16)]</c>;
/// the <c>totp_key_id</c> column on the row identifies which KEK to
/// use on read. Per-row AAD is <c>tenant_id || user_id</c> bytes.
/// </summary>
/// <remarks>
/// <para>KEK material lives in env-vars (<c>Auth:TotpKek:Current</c>
/// + <c>Auth:TotpKek:Previous</c>) per KTD8. Rotation = bump Current,
/// set Previous = old Current, deploy. Reads fall back to Previous
/// when the row's <c>totp_key_id</c> mismatches Current.</para>
///
/// <para>Tampered ciphertext throws
/// <see cref="AuthenticationTagMismatchException"/>; the handler
/// converts that to <c>auth.invalid_credentials</c> per R6. Missing
/// key slot (rotation completed before re-encrypt) returns null —
/// handler surfaces <c>auth.mfa_secret_unrecoverable</c> for ops
/// alerting (rare; Sprint-10+ re-encrypt sweep prevents it).</para>
/// </remarks>
public sealed class AesTotpSecretCipher : ITotpSecretCipher
{
    private const int NonceLength = 12;
    private const int TagLength = 16;
    private const int KeyLength = 32;

    private readonly byte[] _currentKey;
    private readonly byte[]? _previousKey;
    private readonly int _currentKeyId;

    public AesTotpSecretCipher(IOptions<TotpKekOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var opts = options.Value;

        if (string.IsNullOrWhiteSpace(opts.Current))
        {
            throw new InvalidOperationException(
                "Auth:TotpKek:Current is required — generate via 'openssl rand -base64 32'.");
        }
        _currentKey = DecodeKey(opts.Current);
        _previousKey = string.IsNullOrWhiteSpace(opts.Previous) ? null : DecodeKey(opts.Previous);
        _currentKeyId = opts.CurrentKeyId;
    }

    public int CurrentKeyId => _currentKeyId;

    public (byte[] CiphertextBlob, int KeyId) Encrypt(byte[] plaintext, Guid tenantId, Guid userId)
    {
        ArgumentNullException.ThrowIfNull(plaintext);

        var nonce = RandomNumberGenerator.GetBytes(NonceLength);
        var cipher = new byte[plaintext.Length];
        var tag = new byte[TagLength];
        var aad = BuildAad(tenantId, userId);

        using var gcm = new AesGcm(_currentKey, TagLength);
        gcm.Encrypt(nonce, plaintext, cipher, tag, aad);

        // Framed blob: nonce || cipher || tag
        var blob = new byte[NonceLength + cipher.Length + TagLength];
        Buffer.BlockCopy(nonce, 0, blob, 0, NonceLength);
        Buffer.BlockCopy(cipher, 0, blob, NonceLength, cipher.Length);
        Buffer.BlockCopy(tag, 0, blob, NonceLength + cipher.Length, TagLength);
        return (blob, _currentKeyId);
    }

    public byte[]? Decrypt(byte[] ciphertextBlob, int keyId, Guid tenantId, Guid userId)
    {
        ArgumentNullException.ThrowIfNull(ciphertextBlob);
        if (ciphertextBlob.Length <= NonceLength + TagLength)
        {
            return null;
        }

        byte[] key;
        if (keyId == _currentKeyId)
        {
            key = _currentKey;
        }
        else if (_previousKey is not null)
        {
            // KTD8 — rotation slot. Sprint-9 doesn't track multiple
            // historical keys; if a row's keyId is neither Current nor
            // Previous, the row is unrecoverable.
            key = _previousKey;
        }
        else
        {
            return null;
        }

        var nonce = new byte[NonceLength];
        Buffer.BlockCopy(ciphertextBlob, 0, nonce, 0, NonceLength);

        var cipherLength = ciphertextBlob.Length - NonceLength - TagLength;
        var cipher = new byte[cipherLength];
        Buffer.BlockCopy(ciphertextBlob, NonceLength, cipher, 0, cipherLength);

        var tag = new byte[TagLength];
        Buffer.BlockCopy(ciphertextBlob, NonceLength + cipherLength, tag, 0, TagLength);

        var plain = new byte[cipherLength];
        var aad = BuildAad(tenantId, userId);

        using var gcm = new AesGcm(key, TagLength);
        gcm.Decrypt(nonce, cipher, tag, plain, aad);
        return plain;
    }

    private static byte[] DecodeKey(string base64)
    {
        var raw = Convert.FromBase64String(base64);
        if (raw.Length != KeyLength)
        {
            throw new InvalidOperationException(
                $"TOTP KEK must be exactly {KeyLength} bytes (256 bits) after base64 decode; got {raw.Length}.");
        }
        return raw;
    }

    private static byte[] BuildAad(Guid tenantId, Guid userId)
    {
        var aad = new byte[32];
        tenantId.TryWriteBytes(aad.AsSpan(0, 16));
        userId.TryWriteBytes(aad.AsSpan(16, 16));
        return aad;
    }
}
