using System.Text.Json.Serialization;

namespace ShopFlow.Auth.Infrastructure.Storage;

/// <summary>
/// Internal JSON envelope stored at <c>refresh:{tenantSlug}:{userId}:{tokenHashHex}</c>
/// in Redis. Captures everything the rotation handler needs WITHOUT
/// the plaintext refresh token — the plaintext only crosses the wire
/// to/from the client; Redis only ever sees the SHA-256 hash in the
/// key name (KTD3).
/// </summary>
/// <remarks>
/// <para>Carries <see cref="RememberMe"/> so rotation can preserve the
/// original TTL bucket (rotating a 30-day token re-issues another
/// 30-day token, not a fresh 7-day token).</para>
/// </remarks>
internal sealed record RefreshTokenRecord(
    [property: JsonPropertyName("uid")] Guid UserId,
    [property: JsonPropertyName("iat")] DateTime IssuedAt,
    [property: JsonPropertyName("exp")] DateTime ExpiresAt,
    [property: JsonPropertyName("rm")] bool RememberMe);

/// <summary>
/// Tombstone value stored at <c>refresh:rotated:{tenant}:{user}:{oldHashHex}</c>
/// for <see cref="RefreshTokenOptions.RotationGraceWindowSeconds"/>.
/// Points at the successor token's hash so concurrent retries return
/// the same successor (KTD3 grace-window pattern).
/// </summary>
internal sealed record RefreshTokenTombstone(
    [property: JsonPropertyName("nh")] string NextTokenHash,
    [property: JsonPropertyName("nt")] string NextTokenPlaintext);
