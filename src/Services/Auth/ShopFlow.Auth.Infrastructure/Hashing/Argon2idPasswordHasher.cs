using System.Security.Cryptography;
using System.Text;
using Konscious.Security.Cryptography;
using Microsoft.Extensions.Options;
using ShopFlow.Auth.Application.Ports;

namespace ShopFlow.Auth.Infrastructure.Hashing;

/// <summary>
/// OWASP-aligned Argon2id implementation of
/// <see cref="IPasswordHasher"/> (Sprint-8 U4 / R17 + R18). Plaintext
/// passwords never leave this class — the hash never embeds the
/// plaintext, the verify path uses fixed-time comparison, and no
/// failure mode leaks parameter or length information.
/// </summary>
/// <remarks>
/// <para>Output PHC modular string is the Argon2 RFC 9106 shape:</para>
/// <code>
/// $argon2id$v=19$m=&lt;memKiB&gt;,t=&lt;iter&gt;,p=&lt;par&gt;$&lt;base64-salt&gt;$&lt;base64-hash&gt;
/// </code>
/// <para>The parameters are EMBEDDED in the stored hash, not configured
/// only at verify time, so future tuning is safe — Sprint-9+ can roll
/// new defaults (e.g. memory 64 MiB → 96 MiB) without forcing any
/// existing user to reset their password. Verify reads the parameters
/// off the stored string + re-runs Argon2 with those exact values.</para>
///
/// <para><see cref="Verify"/> returns <c>false</c> for any malformed
/// or unrecognised hash format. The login handler at U7 collapses both
/// "wrong password" + "corrupted hash" + "future-format hash" to a
/// single <c>auth.invalid_credentials</c> 401 path, so this method must
/// not throw on any input pattern.</para>
/// </remarks>
public sealed class Argon2idPasswordHasher : IPasswordHasher
{
    private const string Algorithm = "argon2id";
    private const int Version = 19;
    private const int SaltLengthBytes = 16;

    private readonly Argon2Options _options;

    public Argon2idPasswordHasher(IOptions<Argon2Options> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options.Value;
    }

    public string Hash(string plaintext, Argon2Profile profile = Argon2Profile.Password)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(plaintext);

        // Sprint-9 U3 — pick the parameter set for the requested profile.
        // The PHC string parameter-embedding lets Verify recover the
        // right params from the stored hash without knowing the profile
        // (Sprint-8 KTD4 preserved).
        var (memoryKib, iterations, parallelism, hashLen) = profile switch
        {
            Argon2Profile.RecoveryCode => (
                _options.RecoveryCode.MemorySizeKib,
                _options.RecoveryCode.Iterations,
                _options.RecoveryCode.DegreeOfParallelism,
                _options.RecoveryCode.HashLengthBytes
            ),
            _ => (
                _options.MemorySizeKib,
                _options.Iterations,
                _options.DegreeOfParallelism,
                _options.HashLengthBytes
            ),
        };

        var salt = RandomNumberGenerator.GetBytes(SaltLengthBytes);
        var hash = ComputeHash(plaintext, salt, memoryKib, iterations, parallelism, hashLen);

        return string.Create(
            System.Globalization.CultureInfo.InvariantCulture,
            $"${Algorithm}$v={Version}$m={memoryKib},t={iterations},p={parallelism}${Convert.ToBase64String(salt)}${Convert.ToBase64String(hash)}"
        );
    }

    public bool Verify(string plaintext, string phcHash)
    {
        if (string.IsNullOrWhiteSpace(plaintext) || string.IsNullOrWhiteSpace(phcHash))
        {
            return false;
        }

        if (!TryParsePhc(phcHash, out var parsed))
        {
            return false;
        }

        try
        {
            var computed = ComputeHash(
                plaintext,
                parsed.Salt,
                parsed.MemoryKib,
                parsed.Iterations,
                parsed.Parallelism,
                parsed.Hash.Length
            );

            return CryptographicOperations.FixedTimeEquals(computed, parsed.Hash);
        }
        catch (Exception)
        {
            // Any internal Konscious failure (parameter out of supported
            // range, allocation failure) collapses to "not verified" so
            // upstream handler returns auth.invalid_credentials, not 500.
            return false;
        }
    }

    private static byte[] ComputeHash(
        string plaintext,
        byte[] salt,
        int memoryKib,
        int iterations,
        int parallelism,
        int hashLengthBytes
    )
    {
        using var argon2 = new Argon2id(Encoding.UTF8.GetBytes(plaintext))
        {
            Salt = salt,
            MemorySize = memoryKib,
            Iterations = iterations,
            DegreeOfParallelism = parallelism,
        };
        return argon2.GetBytes(hashLengthBytes);
    }

    private static bool TryParsePhc(string phc, out ParsedPhc parsed)
    {
        parsed = default;

        // Expected shape: $argon2id$v=19$m=<mem>,t=<iter>,p=<par>$<salt>$<hash>
        // 6 segments after splitting on '$' (leading empty, algo, version,
        // params, salt, hash).
        var parts = phc.Split('$');
        if (parts.Length != 6)
        {
            return false;
        }

        if (parts[1] != Algorithm)
        {
            return false;
        }

        if (
            !parts[2].StartsWith("v=", StringComparison.Ordinal)
            || !int.TryParse(
                parts[2].AsSpan(2),
                System.Globalization.NumberStyles.Integer,
                System.Globalization.CultureInfo.InvariantCulture,
                out var version
            )
            || version != Version
        )
        {
            return false;
        }

        if (!TryParseParams(parts[3], out var mem, out var iter, out var par))
        {
            return false;
        }

        byte[] salt;
        byte[] hash;
        try
        {
            salt = Convert.FromBase64String(parts[4]);
            hash = Convert.FromBase64String(parts[5]);
        }
        catch (FormatException)
        {
            return false;
        }

        if (salt.Length == 0 || hash.Length == 0)
        {
            return false;
        }

        parsed = new ParsedPhc(mem, iter, par, salt, hash);
        return true;
    }

    private static bool TryParseParams(string segment, out int mem, out int iter, out int par)
    {
        mem = 0;
        iter = 0;
        par = 0;

        var pairs = segment.Split(',');
        if (pairs.Length != 3)
        {
            return false;
        }

        foreach (var pair in pairs)
        {
            var kv = pair.Split('=');
            if (kv.Length != 2)
            {
                return false;
            }
            if (
                !int.TryParse(
                    kv[1],
                    System.Globalization.NumberStyles.Integer,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out var value
                )
                || value <= 0
            )
            {
                return false;
            }
            switch (kv[0])
            {
                case "m":
                    mem = value;
                    break;
                case "t":
                    iter = value;
                    break;
                case "p":
                    par = value;
                    break;
                default:
                    return false;
            }
        }

        return mem > 0 && iter > 0 && par > 0;
    }

    private readonly record struct ParsedPhc(
        int MemoryKib,
        int Iterations,
        int Parallelism,
        byte[] Salt,
        byte[] Hash
    );
}
