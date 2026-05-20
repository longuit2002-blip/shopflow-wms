using ShopFlow.Auth.Application.Ports;
using StackExchange.Redis;

namespace ShopFlow.Auth.Infrastructure.Storage;

/// <summary>
/// Sprint-9 U4 Redis-backed impl of <see cref="IEnrollmentSecretStore"/>.
/// Holds in-flight TOTP enrollment secrets for 10 minutes per KTD10;
/// abandoned enrollments auto-expire via Redis TTL.
/// </summary>
/// <remarks>
/// <para>Key shape:
/// <c>auth:totpenroll:{tenantSlug}:{userId}:{enrollmentId}</c>. The
/// enrollment_id is a fresh Guid returned from <see cref="StoreAsync"/>
/// and echoed back in <see cref="ConsumeAsync"/> — binds the verify
/// call to the issued secret + makes replay-of-already-consumed
/// surface as a missing key (DEL'd) rather than a successful read.</para>
///
/// <para><see cref="ConsumeAsync"/> uses a tiny Lua atomic GET+DEL so
/// concurrent consumers converge — exactly one sees the bytes; the
/// loser sees null. Mirrors the Sprint-8 RedisRefreshTokenStore
/// rotation Lua atomicity pattern.</para>
/// </remarks>
public sealed class RedisEnrollmentSecretStore : IEnrollmentSecretStore
{
    private const string KeyPrefix = "auth:totpenroll";
    private static readonly TimeSpan Ttl = TimeSpan.FromMinutes(10);

    /// <summary>Atomic GET + DEL. Returns the secret bytes on first read; nil after.</summary>
    internal const string ConsumeScript = """
        local v = redis.call('GET', KEYS[1])
        if not v then return nil end
        redis.call('DEL', KEYS[1])
        return v
        """;

    private readonly IConnectionMultiplexer _redis;

    public RedisEnrollmentSecretStore(IConnectionMultiplexer redis)
    {
        ArgumentNullException.ThrowIfNull(redis);
        _redis = redis;
    }

    public async Task<Guid> StoreAsync(string tenantSlug, Guid userId, byte[] secret, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantSlug);
        ArgumentNullException.ThrowIfNull(secret);
        if (secret.Length == 0)
        {
            throw new ArgumentException("Secret bytes are required.", nameof(secret));
        }

        var enrollmentId = Guid.NewGuid();
        var key = BuildKey(tenantSlug, userId, enrollmentId);
        var db = _redis.GetDatabase();
        await db
            .StringSetAsync(key, secret, Ttl)
            .ConfigureAwait(false);
        return enrollmentId;
    }

    public async Task<byte[]?> ConsumeAsync(
        string tenantSlug,
        Guid userId,
        Guid enrollmentId,
        CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantSlug);

        var key = BuildKey(tenantSlug, userId, enrollmentId);
        var db = _redis.GetDatabase();
        var result = await db
            .ScriptEvaluateAsync(
                ConsumeScript,
                new RedisKey[] { key })
            .ConfigureAwait(false);

        if (result.IsNull)
        {
            return null;
        }
        return (byte[]?)result;
    }

    private static string BuildKey(string tenantSlug, Guid userId, Guid enrollmentId) =>
        $"{KeyPrefix}:{tenantSlug}:{userId:N}:{enrollmentId:N}";
}
