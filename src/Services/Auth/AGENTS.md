# Auth module — agent invariants

Sprint-8 + Sprint-9 ship the real auth surface. Plaintext credentials cross the `IPasswordHasher` boundary only; rest of the system handles PHC strings, opaque refresh tokens, and AES-GCM-encrypted TOTP blobs. Sprint-10 lifts the gating mechanism on `AuthAdminController` from class-level `[Authorize(Roles="Owner")]` to per-action `[Authorize(Policy = PermissionKeys.X)]`.

## Hard rules

- **Sprint-10 gating shape**: `AuthAdminController`'s 9 actions carry per-action `[Authorize(Policy = PermissionKeys.X)]` referencing the 9 keys in `PermissionKeys.OwnerCritical`. Class-level `[Authorize(Roles="Owner")]` is removed. Safety nets that prevent Owner lockout: `RolePermissionsSeed` (Sprint-9 U12) bootstraps Owner with all 24 keys at every tenant provision; KTD13 `OwnerCritical` guard in `RolePermissionsCommandHandler` rejects any edit that would shed an admin key from Owner; Sprint-10 U4 `AuthAdminAuthorizePolicyCoverageTests` dual-pins the AuthAdmin policy set against `PermissionKeys.OwnerCritical`. `AuthController` self-service endpoints (logout / me-password / mfa-enroll-begin / mfa-disable / mfa-recovery-codes) keep bare `[Authorize]` — no perm key for self-service over the authenticated user, by design.
- **R6 enumeration discipline**: every credential failure leg collapses to `auth.invalid_credentials` + 401. No leg leaks "missing user" vs "wrong password" vs "locked" vs "tenant unknown" — see `LoginCommandHandler` + `ResolveTenantAsync` in `AuthController` for the canonical shape.
- **KTD5 single-source config**: `Auth:DevSecret` + `Auth:Issuer` + `Auth:Audience` are read by JwtTokenIssuer, the kernel JwtBearer validator, AND the HMAC MFA challenge codec. Bump one and you bump all three.
- **KTD1 perm claim shape**: `perm` is a JSON string array (one `Claim("perm", value)` per key). The U7 policy registration uses `RequireClaim("perm", <key>)` which matches element-by-element. Do NOT space-delimit.
- **KTD2 chain-only revoke**: post-grace refresh-token reuse detection calls `IRefreshTokenStore.RevokeChainAsync(chain_id)`, NOT `RevokeAllForUserAsync`. Other devices on independent chains keep working.
- **KTD3 tombstone TTL 7d**: grace check is code-level `now - RotatedAt < RotationGraceWindowSeconds`, NOT Redis TTL expiry. Tombstone payload carries `chain_id` + `rotated_at`.
- **KTD7 ForwardedHeaders gate**: non-Development boot throws when `Auth:ForwardedHeaders:KnownProxies` and `:KnownNetworks` are both empty. Forge-resistant.
- **KTD8 KEK rotation**: TOTP secret blobs carry `totp_key_id`. Cipher reads `Current` then falls back to `Previous`. Per-row AAD = `tenant_id || user_id`.
- **KTD9 Argon2 dual-profile**: passwords use OWASP 2026 (m=64 MiB / t=4 / p=4); recovery codes use the lighter profile (m=8 MiB / t=2 / p=1). PHC string parameter-embedding lets Verify stay profile-blind.
- **KTD10 enrollment Redis 10-min TTL**: in-flight TOTP secrets live in Redis only. Never in JWT, never in process memory (modular monolith multi-instance under Aspire).
- **KTD13 OwnerCritical guard**: `RolePermissionsCommandHandler` rejects any edit that would leave Owner missing any `PermissionKeys.OwnerCritical` key. Server-side enforced.
- **KTD14 forgot-password constant-time**: unknown email + cooldown active both run a dummy Argon2 verify against `AuthPasswordResetOptions.SyntheticHash` to keep wall-time uniform.
- **KTD16 QR `Cache-Control: no-store`**: the otpauth SVG contains the shared TOTP secret. Any cache layer that persists it = secret leak.
- **R17 Owner-MFA invariant**: `AdminMfaResetCommand` rejects targeting an Owner with `MfaRequired=true` (`auth.mfa_required_invariant_owner`).
- **ADR-0003**: per-tenant DB hard isolation. No `tenant_id` column on any Auth table. Tenant context flows through `IRequestContext` + the per-request `AuthDbContext` binding.

## Pointers

- Domain: `ShopFlow.Auth.Domain` (User aggregate + 5 Sprint-9 entities + UserRole enum + 6 events).
- Application ports: `ShopFlow.Auth.Application.Ports` (12 ports incl. IMfaChallengeTokenCodec, ITotpProvider, ITotpSecretCipher, IAuthOutbox).
- Infrastructure: `ShopFlow.Auth.Infrastructure` (EF context, migrations, Argon2idPasswordHasher, JwtTokenIssuer, RedisRefreshTokenStore chain-aware, OtpNetTotpProvider, AesTotpSecretCipher, HmacMfaChallengeTokenCodec).
- Outbox table: `auth_outbox_messages` (per-module prefix per Sprint-2.5 convention). 4 Sprint-9 contracts published via outbox: `PasswordResetRequestedV1`, `RefreshReuseDetectedV1`, `AccountLockedV1`, `MfaEnrolledV1`.
- Tests: `tests/ShopFlow.Auth.UnitTests/` (167+ tests). Integration tests skip locally without Docker; CI runs the full Aspire-managed suite.
