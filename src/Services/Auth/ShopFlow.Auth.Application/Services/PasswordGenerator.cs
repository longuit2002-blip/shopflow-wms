using System.Security.Cryptography;

namespace ShopFlow.Auth.Application.Services;

/// <summary>
/// Generates strong temporary passwords for the Sprint-8 U8
/// admin-onboarding flow (CreateUser + ResetPassword). 16 chars,
/// URL-safe alphabet, no visually-ambiguous characters.
/// </summary>
/// <remarks>
/// <para>Alphabet excludes:</para>
/// <list type="bullet">
///   <item><description><c>0</c> / <c>O</c> / <c>o</c> — zero vs Os.</description></item>
///   <item><description><c>1</c> / <c>l</c> / <c>I</c> — one vs lowercase L vs uppercase i.</description></item>
/// </list>
/// <para>Symbol set kept conservative (<c>!@#$%^&amp;*+-_</c>) so the
/// temporary password is paste-safe across terminals, browser
/// password managers, and rich-text email clients (won't be mangled
/// by curly-quote substitution).</para>
///
/// <para>Per-position selection uses
/// <see cref="RandomNumberGenerator.GetInt32(int)"/> for cryptographic
/// randomness. The output is then post-shuffled to avoid any positional
/// bias from the category-injection step.</para>
/// </remarks>
public sealed class PasswordGenerator : IPasswordGenerator
{
    public const int Length = 16;
    private const string Lowercase = "abcdefghijkmnpqrstuvwxyz"; // no l
    private const string Uppercase = "ABCDEFGHJKLMNPQRSTUVWXYZ"; // no I, O
    private const string Digits = "23456789"; // no 0, 1
    private const string Symbols = "!@#$%^&*+-_";
    private const string Alphabet = Lowercase + Uppercase + Digits + Symbols;

    public string Generate()
    {
        // Inject one character from each of the 4 categories first to
        // guarantee the "mixed letters/digits/symbols" requirement (R18
        // — eventual policy hook; today the bar is the OWASP minimum +
        // category mix).
        var buf = new char[Length];
        buf[0] = Lowercase[RandomNumberGenerator.GetInt32(Lowercase.Length)];
        buf[1] = Uppercase[RandomNumberGenerator.GetInt32(Uppercase.Length)];
        buf[2] = Digits[RandomNumberGenerator.GetInt32(Digits.Length)];
        buf[3] = Symbols[RandomNumberGenerator.GetInt32(Symbols.Length)];

        for (var i = 4; i < Length; i++)
        {
            buf[i] = Alphabet[RandomNumberGenerator.GetInt32(Alphabet.Length)];
        }

        // Fisher-Yates shuffle with cryptographic indices so the
        // category-injection positions are not deterministic.
        for (var i = buf.Length - 1; i > 0; i--)
        {
            var j = RandomNumberGenerator.GetInt32(i + 1);
            (buf[i], buf[j]) = (buf[j], buf[i]);
        }

        return new string(buf);
    }
}

/// <summary>
/// Seam so the admin handlers can be unit-tested against a
/// deterministic substitute. Kept in Application (not Infrastructure)
/// because the impl has zero infrastructure dependencies — pure
/// cryptographic RNG.
/// </summary>
public interface IPasswordGenerator
{
    string Generate();
}
