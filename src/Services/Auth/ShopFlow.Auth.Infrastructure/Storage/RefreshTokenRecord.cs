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
/// <para>Sprint-9 grafts <see cref="ChainId"/> for chain-aware reuse
/// detection (KTD2). Every login mints a fresh chain_id; rotation
/// propagates it from predecessor to successor; reuse-detection
/// post-grace replay calls
/// <see cref="ShopFlow.Auth.Application.Ports.IRefreshTokenStore.RevokeChainAsync"/>
/// with the bound chain_id to wipe just that chain's live tokens.</para>
///
/// <para>Sprint-8 records (deployed before Sprint-9) carry no
/// <c>ChainId</c> field — the JSON deserialisation defaults to
/// <c>Guid.Empty</c> in that case. The store treats <c>Guid.Empty</c>
/// as a legacy record and falls back to all-user-session revocation
/// to preserve the Sprint-8 safety net during rolling deploy.</para>
/// </remarks>
internal sealed record RefreshTokenRecord(
    [property: JsonPropertyName("uid")] Guid UserId,
    [property: JsonPropertyName("iat")] DateTime IssuedAt,
    [property: JsonPropertyName("exp")] DateTime ExpiresAt,
    [property: JsonPropertyName("rm")] bool RememberMe,
    [property: JsonPropertyName("cid")] Guid ChainId);

/// <summary>
/// Tombstone value stored at <c>refresh:rotated:{tenant}:{user}:{oldHashHex}</c>
/// for <see cref="RefreshTokenOptions.TombstoneTtlSeconds"/> (Sprint-9
/// = 7 days; Sprint-8 was 60 seconds). The longer Sprint-9 TTL is the
/// durable window in which a post-grace replay can still be detected
/// and trigger chain revocation; the grace check itself remains a
/// code-level <c>now - RotatedAt &lt; RotationGraceWindowSeconds</c>
/// comparison (KTD3).
/// </summary>
internal sealed record RefreshTokenTombstone(
    [property: JsonPropertyName("nh")] string NextTokenHash,
    [property: JsonPropertyName("nt")] string NextTokenPlaintext,
    [property: JsonPropertyName("cid")] Guid ChainId,
    [property: JsonPropertyName("rot")] DateTime RotatedAt);
