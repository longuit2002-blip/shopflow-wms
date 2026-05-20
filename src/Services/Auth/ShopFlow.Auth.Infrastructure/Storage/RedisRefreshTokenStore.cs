using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using ShopFlow.Auth.Application.Ports;
using StackExchange.Redis;

namespace ShopFlow.Auth.Infrastructure.Storage;

/// <summary>
/// Redis-backed <see cref="IRefreshTokenStore"/> — Sprint-8 U5.
/// Implements the OWASP refined refresh-token-rotation pattern with
/// a configurable grace-window tombstone (KTD3 + ADV-002 mitigation).
/// </summary>
/// <remarks>
/// <para>Plaintext refresh tokens are URL-safe base64-encodings of 32
/// random bytes. Redis only ever sees the SHA-256 hash in the key
/// name; the plaintext value crosses the wire to the client and
/// nowhere else. Tombstone values DO embed the successor plaintext
/// so concurrent retries get the same successor — accepted leak per
/// the threat model (the rest of the auth state is also in Redis).</para>
///
/// <para>Key namespace per <see cref="IRefreshTokenStore"/> remarks:</para>
/// <list type="bullet">
///   <item><description><c>refresh:{tenantSlug}:{userId}:{tokenHashHex}</c>
///   — live refresh token. TTL = 7d or 30d (rememberMe).</description></item>
///   <item><description><c>refresh:rotated:{tenantSlug}:{userId}:{oldTokenHashHex}</c>
///   — grace-window tombstone. TTL = 60s. Value is JSON pointing at
///   the successor token's plaintext + hash.</description></item>
/// </list>
///
/// <para>Atomicity for the rotation flow is provided by a Lua script
/// (server-side single-shot). Reuse-detection at the cross-chain
/// level (a token presented again AFTER its grace window) currently
/// returns <see cref="RefreshRotateOutcome.NotFound"/> rather than
/// <see cref="RefreshRotateOutcome.ReuseDetected"/> — Sprint-9
/// hardening lands chain-aware reuse detection (binds to client
/// fingerprint + extends tombstone TTL). The single-session-logout
/// produced by <c>NotFound</c> is the safe default the OWASP pattern
/// recommends when stale-vs-replayed can't be distinguished.</para>
/// </remarks>
public sealed class RedisRefreshTokenStore : IRefreshTokenStore
{
    private const int TokenBytes = 32;
    private const string LiveKeyPrefix = "refresh";
    private const string TombstonePrefix = "refresh:rotated";
    private const string RevokeAllMarkerPrefix = "refresh:revoked";

    /// <summary>
    /// Atomic rotation Lua. KEYS[1] = old live key; KEYS[2] = new
    /// live key; KEYS[3] = tombstone key. ARGV[1] = new record JSON;
    /// ARGV[2] = TTL ms (carries the old record's bucket through to
    /// the new key); ARGV[3] = tombstone JSON (next-token pointer);
    /// ARGV[4] = grace window ms. Returns 1 on rotation, nil when the
    /// old key is missing.
    /// </summary>
    internal const string RotateScript = """
        local old = redis.call('GET', KEYS[1])
        if not old then return nil end
        redis.call('DEL', KEYS[1])
        redis.call('SET', KEYS[2], ARGV[1], 'PX', ARGV[2])
        redis.call('SET', KEYS[3], ARGV[3], 'PX', ARGV[4])
        return 1
        """;

    private readonly IConnectionMultiplexer _redis;
    private readonly RefreshTokenOptions _options;

    public RedisRefreshTokenStore(
        IConnectionMultiplexer redis,
        IOptions<RefreshTokenOptions> options)
    {
        ArgumentNullException.ThrowIfNull(redis);
        ArgumentNullException.ThrowIfNull(options);
        _redis = redis;
        _options = options.Value;
    }

    public async Task<string> IssueAsync(
        string tenantSlug,
        Guid userId,
        bool rememberMe,
        CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantSlug);

        var plaintext = NewToken();
        var hashHex = HashHex(plaintext);
        var now = DateTime.UtcNow;
        var ttl = TtlFor(rememberMe);
        var record = new RefreshTokenRecord(userId, now, now.Add(ttl), rememberMe);

        var db = _redis.GetDatabase();
        await db.StringSetAsync(
            LiveKey(tenantSlug, userId, hashHex),
            JsonSerializer.Serialize(record),
            ttl).ConfigureAwait(false);

        return plaintext;
    }

    public async Task<RefreshRotateResult> RotateAsync(
        string tenantSlug,
        Guid userId,
        string presentedToken,
        CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantSlug);
        ArgumentException.ThrowIfNullOrWhiteSpace(presentedToken);

        var oldHashHex = HashHex(presentedToken);
        var db = _redis.GetDatabase();
        var oldKey = LiveKey(tenantSlug, userId, oldHashHex);

        // Pre-flight read of the old record so we can carry the
        // rememberMe TTL bucket through rotation. The Lua handles the
        // atomicity; this read is best-effort (a parallel rotation
        // that won the race will see the Lua return nil here).
        var existingJson = await db.StringGetAsync(oldKey).ConfigureAwait(false);
        if (!existingJson.HasValue)
        {
            // Look up the tombstone — concurrent-retry grace replay.
            var tombKey = TombstoneKey(tenantSlug, userId, oldHashHex);
            var tombJson = await db.StringGetAsync(tombKey).ConfigureAwait(false);
            if (tombJson.HasValue)
            {
                var tomb = JsonSerializer.Deserialize<RefreshTokenTombstone>(tombJson!);
                if (tomb is not null)
                {
                    return new RefreshRotateResult(
                        RefreshRotateOutcome.GraceReplay, tomb.NextTokenPlaintext);
                }
            }

            return new RefreshRotateResult(RefreshRotateOutcome.NotFound, null);
        }

        var existing = JsonSerializer.Deserialize<RefreshTokenRecord>(existingJson!);
        if (existing is null)
        {
            return new RefreshRotateResult(RefreshRotateOutcome.NotFound, null);
        }

        var newPlain = NewToken();
        var newHashHex = HashHex(newPlain);
        var now = DateTime.UtcNow;
        var ttl = TtlFor(existing.RememberMe);
        var newRecord = new RefreshTokenRecord(userId, now, now.Add(ttl), existing.RememberMe);
        var tombstone = new RefreshTokenTombstone(newHashHex, newPlain);

        var newKey = LiveKey(tenantSlug, userId, newHashHex);
        var tombstoneKey = TombstoneKey(tenantSlug, userId, oldHashHex);

        var rotated = (int?)await db.ScriptEvaluateAsync(
            RotateScript,
            new RedisKey[] { oldKey, newKey, tombstoneKey },
            new RedisValue[]
            {
                JsonSerializer.Serialize(newRecord),
                (long)ttl.TotalMilliseconds,
                JsonSerializer.Serialize(tombstone),
                _options.RotationGraceWindowSeconds * 1_000,
            }).ConfigureAwait(false);

        if (rotated is null)
        {
            // Lost the race — a parallel rotation just deleted the old
            // key. Fall back to the tombstone path so the loser gets
            // the same successor token the winner produced.
            var tombKey = TombstoneKey(tenantSlug, userId, oldHashHex);
            var tombJson = await db.StringGetAsync(tombKey).ConfigureAwait(false);
            if (tombJson.HasValue)
            {
                var tomb = JsonSerializer.Deserialize<RefreshTokenTombstone>(tombJson!);
                if (tomb is not null)
                {
                    return new RefreshRotateResult(
                        RefreshRotateOutcome.GraceReplay, tomb.NextTokenPlaintext);
                }
            }
            return new RefreshRotateResult(RefreshRotateOutcome.NotFound, null);
        }

        return new RefreshRotateResult(RefreshRotateOutcome.Issued, newPlain);
    }

    public async Task RevokeAsync(
        string tenantSlug,
        Guid userId,
        string token,
        CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantSlug);
        ArgumentException.ThrowIfNullOrWhiteSpace(token);

        var hashHex = HashHex(token);
        var db = _redis.GetDatabase();
        await db.KeyDeleteAsync(LiveKey(tenantSlug, userId, hashHex)).ConfigureAwait(false);
        // Best-effort tombstone delete — a hostile replay of the
        // just-revoked token should not be able to grace-replay back
        // into a fresh successor.
        await db.KeyDeleteAsync(TombstoneKey(tenantSlug, userId, hashHex)).ConfigureAwait(false);
    }

    public async Task RevokeAllForUserAsync(
        string tenantSlug,
        Guid userId,
        CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantSlug);
        var endpoints = _redis.GetEndPoints();
        var pattern = $"{LiveKeyPrefix}:{tenantSlug}:{userId}:*";
        var tombPattern = $"{TombstonePrefix}:{tenantSlug}:{userId}:*";
        var db = _redis.GetDatabase();

        foreach (var endpoint in endpoints)
        {
            var server = _redis.GetServer(endpoint);
            if (server.IsReplica)
            {
                continue;
            }
            await foreach (var key in server.KeysAsync(pattern: pattern).WithCancellation(ct).ConfigureAwait(false))
            {
                await db.KeyDeleteAsync(key).ConfigureAwait(false);
            }
            await foreach (var key in server.KeysAsync(pattern: tombPattern).WithCancellation(ct).ConfigureAwait(false))
            {
                await db.KeyDeleteAsync(key).ConfigureAwait(false);
            }
        }

        // Brief revoke marker so a /refresh in the immediate grace
        // window sees a deliberate revocation, not an opaque
        // missing-key. Consumers (Sprint-9 telemetry) can read this
        // to distinguish revoked vs expired.
        await db.StringSetAsync(
            $"{RevokeAllMarkerPrefix}:{tenantSlug}:{userId}",
            DateTime.UtcNow.ToString("O", System.Globalization.CultureInfo.InvariantCulture),
            TimeSpan.FromSeconds(_options.RotationGraceWindowSeconds)).ConfigureAwait(false);
    }

    private TimeSpan TtlFor(bool rememberMe) =>
        rememberMe
            ? TimeSpan.FromDays(_options.RememberMeTtlDays)
            : TimeSpan.FromDays(_options.RefreshTtlDays);

    private static string LiveKey(string tenantSlug, Guid userId, string hashHex) =>
        $"{LiveKeyPrefix}:{tenantSlug}:{userId}:{hashHex}";

    private static string TombstoneKey(string tenantSlug, Guid userId, string hashHex) =>
        $"{TombstonePrefix}:{tenantSlug}:{userId}:{hashHex}";

    private static string NewToken()
    {
        var bytes = RandomNumberGenerator.GetBytes(TokenBytes);
        // URL-safe base64 without padding (44 chars → 43 chars) so the
        // token can ride in a URL query parameter or be embedded in a
        // JSON string without escaping.
        return Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    private static string HashHex(string plaintext)
    {
        Span<byte> hash = stackalloc byte[32];
        SHA256.HashData(Encoding.UTF8.GetBytes(plaintext), hash);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
