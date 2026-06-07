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
        IOptions<RefreshTokenOptions> options
    )
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
        CancellationToken ct
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantSlug);

        var plaintext = NewToken();
        var hashHex = HashHex(plaintext);
        var now = DateTime.UtcNow;
        var ttl = TtlFor(rememberMe);
        // Sprint-9 KTD2 — fresh chain_id for each login. Subsequent
        // rotations propagate this through the chain; reuse detection
        // wipes the chain on post-grace replay.
        var chainId = Guid.NewGuid();
        var record = new RefreshTokenRecord(userId, now, now.Add(ttl), rememberMe, chainId);

        var db = _redis.GetDatabase();
        await db.StringSetAsync(
                LiveKey(tenantSlug, userId, hashHex),
                JsonSerializer.Serialize(record),
                ttl
            )
            .ConfigureAwait(false);

        return plaintext;
    }

    public async Task<RefreshRotateResult> RotateAsync(
        string tenantSlug,
        Guid userId,
        string presentedToken,
        CancellationToken ct
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantSlug);
        ArgumentException.ThrowIfNullOrWhiteSpace(presentedToken);

        var oldHashHex = HashHex(presentedToken);
        var db = _redis.GetDatabase();
        var oldKey = LiveKey(tenantSlug, userId, oldHashHex);

        // Pre-flight read of the old record so we can carry the
        // rememberMe TTL bucket + chain_id through rotation. The Lua
        // handles atomicity; this read is best-effort (a parallel
        // rotation that won the race will see the Lua return nil here).
        var existingJson = await db.StringGetAsync(oldKey).ConfigureAwait(false);
        if (!existingJson.HasValue)
        {
            // Look up the tombstone — Sprint-9 grace vs post-grace branch.
            return await HandleTombstonePathAsync(tenantSlug, userId, oldHashHex, ct)
                .ConfigureAwait(false);
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
        // Sprint-9 — chain_id propagates from predecessor to successor.
        var chainId = existing.ChainId == Guid.Empty ? Guid.NewGuid() : existing.ChainId;
        var newRecord = new RefreshTokenRecord(
            userId,
            now,
            now.Add(ttl),
            existing.RememberMe,
            chainId
        );
        var tombstone = new RefreshTokenTombstone(newHashHex, newPlain, chainId, now);

        var newKey = LiveKey(tenantSlug, userId, newHashHex);
        var tombstoneKey = TombstoneKey(tenantSlug, userId, oldHashHex);

        var rotated = (int?)
            await db.ScriptEvaluateAsync(
                    RotateScript,
                    new RedisKey[] { oldKey, newKey, tombstoneKey },
                    new RedisValue[]
                    {
                        JsonSerializer.Serialize(newRecord),
                        (long)ttl.TotalMilliseconds,
                        JsonSerializer.Serialize(tombstone),
                        // Sprint-9 — tombstone TTL is now 7d (configurable),
                        // not the 60-sec grace. The grace check moves to
                        // code-level comparison against tombstone.RotatedAt.
                        _options.TombstoneTtlSeconds * 1_000L,
                    }
                )
                .ConfigureAwait(false);

        if (rotated is null)
        {
            // Lost the race — a parallel rotation just deleted the
            // old key. Fall back to the tombstone path so the loser
            // sees the same successor the winner produced.
            return await HandleTombstonePathAsync(tenantSlug, userId, oldHashHex, ct)
                .ConfigureAwait(false);
        }

        return new RefreshRotateResult(RefreshRotateOutcome.Issued, newPlain, chainId);
    }

    /// <summary>
    /// Sprint-9 — tombstone read path. Grace window (now - RotatedAt
    /// &lt; RotationGraceWindowSeconds) returns the cached successor
    /// (GraceReplay); post-grace triggers chain revocation
    /// (ChainRevoked). Legacy Sprint-8 tombstones with ChainId =
    /// Guid.Empty collapse to RevokeAllForUserAsync (back-compat).
    /// </summary>
    private async Task<RefreshRotateResult> HandleTombstonePathAsync(
        string tenantSlug,
        Guid userId,
        string oldHashHex,
        CancellationToken ct
    )
    {
        var db = _redis.GetDatabase();
        var tombKey = TombstoneKey(tenantSlug, userId, oldHashHex);
        var tombJson = await db.StringGetAsync(tombKey).ConfigureAwait(false);
        if (!tombJson.HasValue)
        {
            return new RefreshRotateResult(RefreshRotateOutcome.NotFound, null);
        }

        var tomb = JsonSerializer.Deserialize<RefreshTokenTombstone>(tombJson!);
        if (tomb is null)
        {
            return new RefreshRotateResult(RefreshRotateOutcome.NotFound, null);
        }

        var now = DateTime.UtcNow;
        var graceWindow = TimeSpan.FromSeconds(_options.RotationGraceWindowSeconds);

        if (tomb.ChainId == Guid.Empty)
        {
            // Sprint-8 legacy tombstone — no chain_id field. Preserve
            // the Sprint-8 safety net: any replay against a legacy
            // tombstone, regardless of grace, surfaces as ReuseDetected
            // so the handler can revoke all sessions. Cleaner than
            // silent grace-replay because the legacy 60-sec TTL has
            // already passed by definition (the row survived the
            // Sprint-9 deploy → it's at least the deploy duration old).
            if (now - tomb.RotatedAt < graceWindow)
            {
                return new RefreshRotateResult(
                    RefreshRotateOutcome.GraceReplay,
                    tomb.NextTokenPlaintext
                );
            }
            return new RefreshRotateResult(RefreshRotateOutcome.ReuseDetected, null);
        }

        // Sprint-9 path.
        if (now - tomb.RotatedAt < graceWindow)
        {
            return new RefreshRotateResult(
                RefreshRotateOutcome.GraceReplay,
                tomb.NextTokenPlaintext,
                tomb.ChainId
            );
        }

        // Post-grace replay — chain compromise. Revoke just this chain,
        // not all user sessions (KTD2). Other chains for the same user
        // (parallel logins from different devices) keep working.
        await RevokeChainAsync(tenantSlug, userId, tomb.ChainId, ct).ConfigureAwait(false);
        return new RefreshRotateResult(RefreshRotateOutcome.ChainRevoked, null, tomb.ChainId);
    }

    public async Task RevokeAsync(
        string tenantSlug,
        Guid userId,
        string token,
        CancellationToken ct
    )
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

    public async Task RevokeAllForUserAsync(string tenantSlug, Guid userId, CancellationToken ct)
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
            await foreach (
                var key in server
                    .KeysAsync(pattern: pattern)
                    .WithCancellation(ct)
                    .ConfigureAwait(false)
            )
            {
                await db.KeyDeleteAsync(key).ConfigureAwait(false);
            }
            await foreach (
                var key in server
                    .KeysAsync(pattern: tombPattern)
                    .WithCancellation(ct)
                    .ConfigureAwait(false)
            )
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
                TimeSpan.FromSeconds(_options.RotationGraceWindowSeconds)
            )
            .ConfigureAwait(false);
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
        return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    private static string HashHex(string plaintext)
    {
        Span<byte> hash = stackalloc byte[32];
        SHA256.HashData(Encoding.UTF8.GetBytes(plaintext), hash);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    public async Task RevokeChainAsync(
        string tenantSlug,
        Guid userId,
        Guid chainId,
        CancellationToken ct
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantSlug);
        if (chainId == Guid.Empty)
        {
            return;
        }

        var db = _redis.GetDatabase();
        var endpoints = _redis.GetEndPoints();
        var livePattern = $"{LiveKeyPrefix}:{tenantSlug}:{userId}:*";
        var tombPattern = $"{TombstonePrefix}:{tenantSlug}:{userId}:*";

        foreach (var endpoint in endpoints)
        {
            var server = _redis.GetServer(endpoint);
            if (server.IsReplica)
            {
                continue;
            }

            // SCAN + per-key filter on chain_id. Typical user has < 10
            // active refresh tokens; the cost is bounded by the user's
            // login footprint, not the global key space. Sprint-10+
            // can add a secondary set-index keyed by chain_id if the
            // scan cost matters in practice.
            await foreach (
                var key in server
                    .KeysAsync(pattern: livePattern)
                    .WithCancellation(ct)
                    .ConfigureAwait(false)
            )
            {
                var json = await db.StringGetAsync(key).ConfigureAwait(false);
                if (!json.HasValue)
                {
                    continue;
                }
                var record = JsonSerializer.Deserialize<RefreshTokenRecord>(json!);
                if (record is not null && record.ChainId == chainId)
                {
                    await db.KeyDeleteAsync(key).ConfigureAwait(false);
                }
            }

            // Also revoke matching tombstones — a hostile replay against
            // the predecessor token must not grace-replay into a fresh
            // successor after chain revocation.
            await foreach (
                var key in server
                    .KeysAsync(pattern: tombPattern)
                    .WithCancellation(ct)
                    .ConfigureAwait(false)
            )
            {
                var json = await db.StringGetAsync(key).ConfigureAwait(false);
                if (!json.HasValue)
                {
                    continue;
                }
                var tomb = JsonSerializer.Deserialize<RefreshTokenTombstone>(json!);
                if (tomb is not null && tomb.ChainId == chainId)
                {
                    await db.KeyDeleteAsync(key).ConfigureAwait(false);
                }
            }
        }
    }
}
