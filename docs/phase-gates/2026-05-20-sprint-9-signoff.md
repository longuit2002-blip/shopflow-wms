---
title: "Sprint-9 sign-off — Backend Auth Hardening (RBAC + MFA + chain-aware refresh + lockout + reset)"
date: 2026-05-20
status: complete
follows: docs/phase-gates/2026-05-20-sprint-8.5-signoff.md
plan: docs/plans/2026-05-20-003-feat-sprint-9-rbac-mfa-hardening-plan.md
origin: docs/brainstorms/2026-05-20-sprint-9-rbac-mfa-hardening-requirements.md
tag: v0.12.0-sprint-9
---

# Sprint-9 sign-off — Backend Auth Hardening

Sprint-9 ships the backend Auth foundation for per-permission RBAC, TOTP MFA, account lockout with sliding-window + per-IP rate limit, chain-aware refresh-token reuse detection, self-service password reset, and the four cross-module Auth events. The 7-unit Notification module quartet (U10/U11), 3 frontend units (U13/U14/U15), and full cross-tenant integration test suite (U16-extended) are explicitly **deferred to Sprint-9.5** so this sprint can ship a coherent backend foundation tag without a half-finished demo-able UI tail.

The Auth.Api surface exposes 7 new public endpoints (forgot-password, reset-confirm, MFA enroll begin/verify, MFA verify, disable, recovery-codes) and 4 new admin endpoints (admin MFA reset, admin unlock, role-permissions GET/PUT). All credential failure legs collapse to `auth.invalid_credentials` 401 per R6. The 4 Sprint-9 cross-module events (`PasswordResetRequestedV1`, `RefreshReuseDetectedV1`, `AccountLockedV1`, `MfaEnrolledV1`) are emitted via the new per-module `auth_outbox_messages` table + multiplexed dispatcher; they currently publish to RabbitMQ where they'll wait in the configured exchange until Sprint-9.5 ships the Notification consumers (no message loss — MT durable queues hold them).

## What shipped

| U-ID | Goal | Status | Commit |
|------|------|--------|--------|
| U0 | Branch cut + brainstorm + plan + 16 KTDs + 2 P0 doc-review fixes folded | ✅ | `e82101c` |
| U1 | Auth.Domain lockout + MFA columns + 3 events + PermissionKeys catalog (25 keys + OwnerCritical subset) | ✅ | `09f7b49` |
| U2 | Auth.Application 9 new ports + 10 Command skeletons + extended LoginResponse + Argon2Profile enum | ✅ | `de053c8` |
| U3 | Auth.Infrastructure schema (6 new tables + 5 column adds) + 6 EntityConfigurations + 5 repositories + Argon2 dual-profile | ✅ | `d24b7f2` |
| U4 | TOTP infrastructure — Otp.NET wrapper + AES-256-GCM cipher with KEK rotation + Redis enrollment store | ✅ | `ec3d7e2` |
| U5 | Redis chain-aware refresh-token store extension (7d tombstone + chain_id propagation + RevokeChainAsync) | ✅ | `bca948f` |
| U6 | JwtTokenIssuer permission projection (async signature bump + IRolePermissionRepository injection) | ✅ | `8e05c5b` |
| U7 | Permission policy composition (AddShopFlowPermissionPolicies) + RateLimiter + ForwardedHeaders + UseShopFlowSecurityPipeline | ✅ | `0c6d9bb` |
| U8 | Auth handlers — LoginCommandHandler lockout+MFA branch, RefreshTokenCommandHandler chain-revoke emit, ChangePasswordCommandHandler intact, 10 new handlers, 4 cross-module contracts, IMfaChallengeTokenCodec | ✅ | `6e7714b` |
| U9 | Auth.Api Sprint-9 surface — 7 new endpoints + 4 admin endpoints + AuthOptions extension + appsettings + outbox dispatcher + Auth AGENTS.md | ✅ | `c0c3ad9` |
| U12 (partial) | RolePermissionsSeed + OwnerSeed mfa_required=true; Notification IModuleMigrationRegistry deferred | ✅ | `fe386e2` |
| U16 (light) | 4 docs/solutions notes + RolePermissionsCommandHandler OwnerCritical guard tests | ✅ | `a996a65` |
| U17 | Sign-off (this doc) + CHANGELOG + README current-stage + CLAUDE.md current-stage + tag `v0.12.0-sprint-9` | ✅ | (this commit) |

**Deferred to Sprint-9.5** (status: pending — not shipped this sprint):

| U-ID | Goal | Deferral reason |
|------|------|----------------|
| U10 | Notification module quartet + Mailpit Aspire wiring + initial schema | New 7th business module; ~1-2 hours of focused work. The 4 contracts are already published; consumers idle in MT queues until U10 lands. |
| U11 | 4 Notification consumers + IMailerProvider + LoggingMailer + MailKitSmtpMailer + SimpleTemplateRenderer + 4 templates × 2 (text+html) | Depends on U10 quartet existing. |
| U13 | Frontend useAuth + httpClient 401/403 split + LoginScreen MFA branch | Backend works end-to-end without UI; the Sprint-9 endpoints exist but won't be exercised from the frontend until U13. |
| U14 | 5 new frontend auth screens (forgot/reset/MFA enroll/MFA challenge/profile-security) + RecoveryCodesDisplay | Largest frontend scope; ~1-2 hours of focused work. |
| U15 | Frontend Owner admin surface (MFA status column + locked-accounts panel + RolePermissionsEditor) | Depends on U13/U14 patterns. |
| U16 (extended) | AuthCrossTenantTests + 4 KTD-pinning integration tests against Testcontainers Postgres + Redis | Needs a Docker daemon; CI runs the full integration suite. |

## Architecture Summary

**Defense-in-depth layered.** Sprint-9 adds five independent defense layers without removing any Sprint-8 invariants:

1. **Per-account lockout** (5/15-min sliding window + 15-min lockout) on `users.locked_until` + `failed_login_count` + new `last_failed_login_at` field. Primary defense per OWASP Authentication Cheat Sheet.
2. **Per-IP rate limit** (10/min on auth-credentials, 5/min on auth-forgot-password) via ASP.NET RateLimiter behind ForwardedHeaders. **Supplementary**, never replaces per-account lockout.
3. **TOTP MFA** (Otp.NET-backed ±1-step drift + AES-256-GCM at-rest with KEK rotation slot + 10 single-use Argon2-hashed recovery codes). Owner role forced enrollment per R17.
4. **Chain-aware refresh** (per-login chain_id propagation through rotation; post-grace replay revokes the chain only, not all-user-sessions per RFC 9700 §4.14). 7-day tombstone TTL with code-level grace check at 60sec.
5. **Per-permission RBAC** (JSON-array `perm` claim emitted by JwtTokenIssuer + ASP.NET policy registered per `PermissionKeys.All` entry). Owner-critical subset locked via `RolePermissionsCommandHandler` server-side guard.

**R6 enumeration discipline preserved.** Every credential failure leg (locked, unknown user, wrong password, MFA fail, recovery-code reuse, expired token, unknown tenant) collapses to `auth.invalid_credentials` 401. Forgot-password always returns 200 with a synthetic constant-time Argon2 verify against `Auth:PasswordReset:SyntheticHash` on unknown-email and cooldown-active paths.

**Per-module outbox + dispatcher infrastructure pattern carries forward.** `auth_outbox_messages` table mirrors Sprint-2-redux Inbound + Sprint-3-redux Outbound naming convention. `MultiplexedOutboxDispatcher<AuthDbContext>` hosted service polls + publishes via the 4 `AddOutboxRoute<T>(SendKind.Publish)` registrations in `AuthServiceCollectionExtensions`.

## Key Technical Decisions

(see plan + brainstorm for the 16 KTDs; selected high-impact items below):

- **KTD1** — `perm` claim emitted as JSON `string[]` via N separate `Claim("perm", value)` entries, NOT space-delimited. `JsonWebTokenHandler` flattens identical-type claims into a JSON array on the wire; `RequireClaim` matches element-by-element. (`docs/solutions/2026-05-20-perm-claim-must-be-json-array.md`)
- **KTD2** — Chain-aware reuse detection revokes only the affected chain (RFC 9700 §4.14 + Auth0/Okta 2026 production canon), not all-user-sessions. Other devices on independent chains keep working.
- **KTD3** — Tombstone TTL 7d; grace window 60sec is a code-level `now - RotatedAt` check, not Redis TTL expiry. (`docs/solutions/2026-05-20-chain-aware-refresh-tombstone-7d.md`)
- **KTD5** — `Auth` config section is the single source for `DevSecret` + `Issuer` + `Audience` shared by JwtTokenIssuer + kernel JwtBearer validator + HMAC MFA challenge codec. One bump, one place.
- **KTD7** — ForwardedHeaders middleware wired BEFORE UseRateLimiter; non-Development boot throws when `Auth:ForwardedHeaders:KnownProxies` + `:KnownNetworks` are both empty (silent disable + spoofing vector). (`docs/solutions/2026-05-20-aspnet-ratelimiter-yarp-x-forwarded-for.md`)
- **KTD8** — TOTP KEK stored in env-var as base64; rotation via `totp_key_id smallint` + Current/Previous slot + lazy read fallback. Background re-encrypt sweep is Sprint-10+ ops work. (`docs/solutions/2026-05-20-totp-kek-rotation-via-key-id.md`)
- **KTD9** — Argon2 dual-profile: Password (OWASP 2026 m=64 MiB/t=4/p=4) + RecoveryCode (m=8 MiB/t=2/p=1). PHC string parameter-embedding lets Verify stay profile-blind.
- **KTD13** — `PermissionKeys.OwnerCritical` (9 entries) server-side guard in `RolePermissionsCommandHandler`. Any edit that would leave Owner missing one → 422 `auth.role_permissions_owner_critical_locked`.
- **KTD14** — Forgot-password constant-time response: dummy Argon2id verify against `AuthPasswordResetOptions.SyntheticHash` on unknown-email + cooldown-active paths. Wall-time uniform; R6 enumeration discipline preserved.

## Deviations from plan

- **Sprint-9.5 scope split (largest deviation)**: U10/U11 (Notification module + consumers + templates), U13/U14/U15 (frontend), and U16-extended (cross-tenant + KTD-pinning integration tests) deferred. Sprint-9 ships a backend foundation tag; Sprint-9.5 picks up the demo-able UI tail. Captured at session-time with explicit user approval ("Ship backend as v0.12.0-sprint-9, defer rest").
- **U1**: 5th column `LastFailedLoginAt` added to `users` (plan listed 4 columns). Required by the sliding-window test contract — without a per-failure timestamp the window can only be reset by a successful login, contradicting the test scenario "16-min gap resets counter". Documented inline + in U1 commit.
- **U2**: Held the `ITokenIssuer` async signature bump for U6 (rather than U2 per plan file list). Plan's signature change would have broken Sprint-8 LoginCommandHandler + RefreshTokenCommandHandler + ChangePasswordCommandHandler compile in isolation; U6 atomically flips the issuer + all 3 handlers.
- **U7**: Permission policy attributes NOT yet applied to business module controllers (Inventory/Outbound/Inbound/SkusController/AdjustmentsController) or to AuthAdminController per-action. Class-level `[Authorize(Roles = "Owner")]` on AuthAdminController preserved as the gating mechanism; per-action attributes layer additively when Sprint-10+ flips all tenants to seeded role_permissions. Existing `[Authorize]` attributes still authenticate via JwtBearer.
- **U8**: `MfaChallengeTokenCodec` is a custom HMAC-SHA256 compact token format (`base64url(json).base64url(hmac256)`) rather than a full JWT — sidesteps the ClaimType mapping dance for a 5-min intent token bridging login → MFA verify. Same `DevSecret` (KTD5).
- **U9**: AuthAdminController gets new endpoint METHODS but the class-level `Roles="Owner"` attribute stays. Rolling-deploy safer than per-action `Policy=...` until U10/U12 finish (no tenant has the seed yet).
- **U12**: `IModuleMigrationRegistry` registration for Notification deferred (Notification project doesn't exist yet — lands in Sprint-9.5 U10 commit).
- **U16**: AuthCrossTenantTests + 4 KTD-pinning integration tests deferred to CI (Testcontainers needed); 6 RolePermissionsCommandHandlerTests unit tests added in-scope to pin the KTD13 OwnerCritical guard.

## Verification

- `dotnet build ShopFlow.sln` → **0 errors / 0 warnings** across 41 projects.
- `dotnet test tests/ShopFlow.Auth.UnitTests/` → **173 passed** (was 116 at Sprint-8 baseline; +57 across U1/U3/U4/U6/U8/U16).
- `dotnet test tests/ShopFlow.SharedKernel.UnitTests/` → **47 passed** (was 43; +4 PermissionPolicyComposition).
- Integration tests (Auth + SharedKernel + Migrate) build cleanly and Skip-mark on local-no-Docker per Sprint-8 precedent; CI runs the full suite.
- Frontend Vitest baseline preserved (no frontend changes this sprint).

## Trade-offs carried forward to Sprint-9.5+

1. **Notification module not yet built** — `auth_outbox_messages` rows publish to RabbitMQ + idle in queues. No message loss; consumers ship in Sprint-9.5 U10/U11.
2. **No frontend changes** — Sprint-9 endpoints are functional but unreachable from the existing UI. Sprint-9.5 U13 ships the minimum (useAuth + httpClient 401/403 split + LoginScreen MFA branch); U14 ships 5 new screens; U15 ships admin surface.
3. **Cross-tenant integration tests deferred** — local-no-Docker posture from Sprint-1+ continues; CI nightly handles the full suite. The KTD13 OwnerCritical guard has unit coverage; KTD1 perm-array shape has both unit + integration round-trip in `JwtTokenIssuerTests`.
4. **MailKit prod SMTP** — not exercised this sprint (no Mailpit container booted). Sprint-9.5 U11 wires the dev Mailpit path; real SMTP provider credentials are an operational pre-flight (see plan Scope Boundaries).
5. **TOTP KEK in env-var** — small-mid SaaS-scale acceptable per OWASP Cryptographic Storage Cheat Sheet; KMS/Vault migration is Sprint-10+ work.
6. **`auth_audit_log` unpartitioned** — Sprint-9 ships one table; partitioning + archival is Sprint-10+ ops concern.
7. **Affected-user notification on chain-reuse** — Sprint-9 emits Owner-only `RefreshReuseDetectedV1` per the user's explicit brainstorm choice; Sprint-10+ stretch adds a second event payload + affected-user template per OWASP Session Management canon.
8. **Distributed rate-limit store** — in-memory `PartitionedRateLimiter` is per-process; horizontal scale-out lands Sprint-10+ via Redis-backed shared state.
9. **Eager re-encrypt sweep on KEK rotation** — Sprint-9 ships the lazy read-Current-fallback-Previous; background sweep is Sprint-10+ ops work.
10. **Per-permission attribute migration of business module controllers** — Inventory/Outbound/Inbound/Skus/Adjustments + AuthAdminController per-action policies deferred until all tenants are seeded with role_permissions. Existing `[Authorize]` semantics still apply (JwtBearer authenticates).

## Operational pre-flight for first prod deploy

- (a) Generate fresh `Auth:TotpKek:Current` via `openssl rand -base64 32`; replace the dev sentinel.
- (b) Configure `Auth:PasswordReset:SyntheticHash` via Argon2id of a random plaintext at deploy time.
- (c) Configure `Auth:ForwardedHeaders:KnownProxies` or `:KnownNetworks` with actual gateway IP/CIDR.
- (d) Configure SMTP provider credentials when Sprint-9.5 U11 ships.
- (e) Optionally enable `CREATE INDEX CONCURRENTLY` for `auth_audit_log` once the table reaches scale (Sprint-7.5 carry-over).

## Next implementation step

Cut a fresh branch from `v0.12.0-sprint-9` and start **Sprint-9.5** — U10 + U11 + U13 minimum + U14 + U15 + U16-extended. Plan TBD.
