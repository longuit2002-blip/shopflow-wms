namespace ShopFlow.Auth.Application.Ports;

/// <summary>
/// Port for the OWASP-aligned password hasher (Sprint-8 U4 ships the
/// Konscious Argon2id impl). Plaintext only crosses this boundary —
/// the rest of the system handles PHC modular strings.
/// </summary>
/// <remarks>
/// <para>The PHC format embeds the algorithm parameters (memory,
/// iterations, parallelism, salt) in the stored hash, so future parameter
/// tuning never invalidates existing rows — a Sprint-9+ memory bump
/// from 64 MB → 96 MB just means new hashes use the new shape while
/// old hashes still verify under the old parameters they captured.</para>
///
/// <para><see cref="Verify"/> returns <c>false</c> for malformed PHC
/// input rather than throwing; legitimate verification failures and
/// corrupted-storage cases collapse to the same canonical
/// <c>auth.invalid_credentials</c> path in the login handler. The
/// hasher does not log plaintext or hash material.</para>
/// </remarks>
public interface IPasswordHasher
{
    /// <summary>
    /// Hash a plaintext password. Returns a PHC modular string of the
    /// shape <c>$argon2id$v=19$m=&lt;mem&gt;,t=&lt;iter&gt;,p=&lt;par&gt;$&lt;salt&gt;$&lt;hash&gt;</c>.
    /// Each call generates a fresh random salt — two calls with the
    /// same plaintext produce different hashes.
    /// </summary>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="plaintext"/> is null/empty/whitespace.
    /// Callers should validate at request-DTO boundary first.
    /// </exception>
    string Hash(string plaintext);

    /// <summary>
    /// Verify a plaintext against a stored PHC hash. Returns
    /// <c>false</c> on parameter/format mismatch or hash mismatch.
    /// Never throws on malformed input — production storage can carry
    /// legacy or corrupted rows and the auth path must collapse them
    /// to <c>auth.invalid_credentials</c>, not 500.
    /// </summary>
    bool Verify(string plaintext, string phcHash);
}
