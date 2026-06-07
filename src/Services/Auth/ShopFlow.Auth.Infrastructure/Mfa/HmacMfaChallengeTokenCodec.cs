using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using ShopFlow.Auth.Application.Ports;
using ShopFlow.Auth.Infrastructure.Tokens;

namespace ShopFlow.Auth.Infrastructure.Mfa;

/// <summary>
/// Sprint-9 U4/U8 helper — HMAC-SHA256 signed compact token for the
/// MFA challenge / enrollment intermediate step. Custom shape rather
/// than full JWT to keep the payload tiny + sidestep ClaimType
/// mapping. Signed with the same <c>Auth:DevSecret</c> the access-token
/// validator uses (KTD5).
/// </summary>
/// <remarks>
/// Wire format: <c>base64url(json).base64url(hmac256(json, secret))</c>
/// where the JSON payload is
/// <c>{"uid":"&lt;guid&gt;","ts":"&lt;slug&gt;","rm":false,"int":0,"exp":1717250000}</c>.
/// TTL is fixed at 5 minutes from issuance.
/// </remarks>
public sealed class HmacMfaChallengeTokenCodec : IMfaChallengeTokenCodec
{
    private static readonly TimeSpan Ttl = TimeSpan.FromMinutes(5);

    private readonly byte[] _key;

    public HmacMfaChallengeTokenCodec(IOptions<JwtIssuerOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var secret = options.Value.DevSecret;
        var bytes = Encoding.UTF8.GetBytes(secret);
        if (bytes.Length < 32)
        {
            throw new InvalidOperationException(
                "Auth:DevSecret must be at least 32 bytes for HMAC-SHA256 challenge tokens."
            );
        }
        _key = bytes;
    }

    public string Issue(
        Guid userId,
        string tenantSlug,
        bool rememberMe,
        MfaChallengeIntent intent,
        DateTime issuedAt
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantSlug);

        var payload = new ChallengePayload(
            userId.ToString("N"),
            tenantSlug,
            rememberMe,
            (int)intent,
            new DateTimeOffset(issuedAt.Add(Ttl), TimeSpan.Zero).ToUnixTimeSeconds()
        );

        var json = JsonSerializer.Serialize(payload);
        var jsonBytes = Encoding.UTF8.GetBytes(json);
        var sig = HMACSHA256.HashData(_key, jsonBytes);

        return $"{Base64UrlEncode(jsonBytes)}.{Base64UrlEncode(sig)}";
    }

    public MfaChallengePayload? TryDecode(string token, DateTime now)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return null;
        }
        var parts = token.Split('.');
        if (parts.Length != 2)
        {
            return null;
        }

        byte[] jsonBytes;
        byte[] sig;
        try
        {
            jsonBytes = Base64UrlDecode(parts[0]);
            sig = Base64UrlDecode(parts[1]);
        }
        catch (FormatException)
        {
            return null;
        }

        var expected = HMACSHA256.HashData(_key, jsonBytes);
        if (!CryptographicOperations.FixedTimeEquals(expected, sig))
        {
            return null;
        }

        ChallengePayload? payload;
        try
        {
            payload = JsonSerializer.Deserialize<ChallengePayload>(jsonBytes);
        }
        catch (JsonException)
        {
            return null;
        }
        if (payload is null)
        {
            return null;
        }

        var expUtc = DateTimeOffset.FromUnixTimeSeconds(payload.exp).UtcDateTime;
        if (expUtc <= now)
        {
            return null;
        }

        if (!Guid.TryParseExact(payload.uid, "N", out var userId))
        {
            return null;
        }

        return new MfaChallengePayload(
            userId,
            payload.ts,
            payload.rm,
            (MfaChallengeIntent)payload.@int
        );
    }

    private sealed record ChallengePayload(string uid, string ts, bool rm, int @int, long exp);

    private static string Base64UrlEncode(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static byte[] Base64UrlDecode(string s)
    {
        var pad = (4 - s.Length % 4) % 4;
        var b64 = s.Replace('-', '+').Replace('/', '_') + new string('=', pad);
        return Convert.FromBase64String(b64);
    }
}
