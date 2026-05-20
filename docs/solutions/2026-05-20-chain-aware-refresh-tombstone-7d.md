---
title: "Chain-aware refresh tombstone TTL is 7 days, grace check is code-level"
date: 2026-05-20
type: architecture
sprint: 9
units: [U5, U8]
---

## Rule

The Redis tombstone for a rotated refresh token has a TTL matching the refresh token TTL itself (default 7 days, configurable via `Auth:Refresh:TombstoneTtlSeconds`). The 60-sec **grace window** is enforced by a code-level comparison `now - tombstone.RotatedAt < RotationGraceWindowSeconds`, NOT by Redis TTL expiry.

## Why

Two distinct windows operate on the same tombstone row:

1. **Grace window (60s)**: how long after rotation a legitimate concurrent retry can present the just-rotated predecessor and receive the same cached successor (idempotent client retries, multi-tab browsers).
2. **Tombstone TTL (7d)**: how long after rotation the store can still **detect** that a predecessor token was rotated and trigger chain-revocation on post-grace replay.

Sprint-8 conflated the two — tombstones expired after 60s. That meant a stolen refresh token replayed at T+90s would see "no tombstone" and surface as `NotFound` (single-session-logout safety net). With Sprint-9 chain semantics, the tombstone needs to survive the full refresh TTL so post-grace replays trigger chain revocation per RFC 9700 §4.14.

## How to apply

The tombstone payload carries both `ChainId` + `RotatedAt`:

```csharp
internal sealed record RefreshTokenTombstone(
    [property: JsonPropertyName("nh")] string NextTokenHash,
    [property: JsonPropertyName("nt")] string NextTokenPlaintext,
    [property: JsonPropertyName("cid")] Guid ChainId,
    [property: JsonPropertyName("rot")] DateTime RotatedAt);
```

`RedisRefreshTokenStore.HandleTombstonePathAsync` reads the tombstone, computes `now - tomb.RotatedAt`, and branches:

- `< 60s` → `GraceReplay` with cached successor.
- `>= 60s` → `RevokeChainAsync(tomb.ChainId)` + `ChainRevoked` outcome.

## Where it lives

- `src/Services/Auth/ShopFlow.Auth.Infrastructure/Storage/RefreshTokenRecord.cs`.
- `src/Services/Auth/ShopFlow.Auth.Infrastructure/Storage/RefreshTokenOptions.cs` — `TombstoneTtlSeconds = 604_800`, `RotationGraceWindowSeconds = 60`.
- `src/Services/Auth/ShopFlow.Auth.Infrastructure/Storage/RedisRefreshTokenStore.cs#HandleTombstonePathAsync`.
- Rolling-deploy back-compat: legacy Sprint-8 tombstones carry `ChainId = Guid.Empty`; the store collapses to `RevokeAllForUserAsync` for those (single-session-logout safety net) rather than chain-only revoke.

## Reviewers' checklist

- Any change to refresh-token rotation logic must update both windows independently.
- Don't shorten `TombstoneTtlSeconds` below `RefreshTtlDays` — gap creates an exploit window.
