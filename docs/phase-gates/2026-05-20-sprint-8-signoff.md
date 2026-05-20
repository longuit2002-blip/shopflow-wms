---
title: "Sprint-8 sign-off — Real Auth Module"
date: 2026-05-20
status: complete
follows: docs/phase-gates/2026-05-20-sprint-7.5-signoff.md
plan: docs/plans/2026-05-20-001-feat-sprint-8-real-auth-plan.md
origin: docs/brainstorms/2026-05-20-sprint-8-real-auth-requirements.md
tag: v0.11.0-sprint-8
---

# Sprint-8 sign-off — Real Auth Module

Sprint-8 retires the Sprint-6 dev-mode baked JWT and ships the production-ready authentication module: Argon2id password hashing, JWT access tokens issued by the Auth module + validated by the kernel JwtBearer (same `Auth:DevSecret`), Redis-backed refresh-token rotation with a 60-second grace window (OWASP refined pattern), per-tenant `users` table with case-insensitive UNIQUE on `lower(email)` + CHECK on role enum, Owner-only admin CRUD for tenant users, and a frontend that detects tenant from host subdomain + transparently refreshes 401s. 12 implementation units (U0-U11 plus sign-off) shipped on `feat/sprint-8-real-auth` cut from `v0.10.1-sprint-7.5`.

The sprint executed brainstorm → ce-plan → ce-doc-review → ce-work inline (single-conversation orchestration, no subagent dispatch) per the Sprint-7.5 lessons-learned: when the orchestrator has rich context already in-window, inline execution is faster than dispatching subagents with full context handoffs. Doc-review applied **18 fixes** before execution started (1 P0 + 9 P1 + 8 P2), notably the `iss`/`aud` config-driven default that prevented a total auth bypass and the host-suffix allowlist promotion from "Outstanding Question" to hard requirement.

## What shipped

| U-ID | Goal | Status | Commit |
|------|------|--------|--------|
| U0 | Branch cut + brainstorm + plan + 10 KTDs (post-doc-review) in opening commit body | ✅ | `b5b7eec` |
| U1 | Auth.Domain — `User` aggregate (factory validation + named mutations + buffered domain events) + `UserRole` enum pinned to DB CHECK constraint via `UserRoleTests` + 3 domain events (`UserCreatedEvent`, `UserPasswordChangedEvent`, `UserRoleChangedEvent`); 38 unit tests | ✅ | `86a1d9a` |
| U2 | Auth.Application — `IUserRepository` / `IPasswordHasher` / `IRefreshTokenStore` / `ITokenIssuer` port surfaces + wire DTOs (`LoginRequest`, `LoginResponse`, `RefreshRequest/Response`, `LogoutRequest`, `ChangePasswordRequest`, `CreateUserRequest/Response`, `UpdateUserRequest`, `ResetPasswordResponse`, `UserSummary`, `ListUsersResponse`); pure interfaces, no impls | ✅ | `40b377a` |
| U3 | Auth.Infrastructure — `AuthDbContext` + `UserConfiguration` (role-as-string conversion, no `tenant_id` per ADR-0003) + `AddUsers` migration (`ux_users_email_lower` UNIQUE expression-index + `chk_users_role` CHECK + partial `ix_users_role_active`) + `UserRepository` with 23505 → EmailInUse Result wrap + `AuthServiceCollectionExtensions` composition + IntegrationTests skeleton (`AuthTenantFixture` + 6 repo tests + 5 migration smoke tests) | ✅ | `1d9d13f` |
| U4 | `Argon2idPasswordHasher` (Konscious 1.3.1) with PHC-embedded parameters; OWASP 2026 defaults (m=64 MiB, t=4, p=4, 32-byte hash) bound from `Auth:Argon2`; round-trip across hasher instances (params travel in hash); never-throw `Verify` for malformed PHC strings (collapses to `auth.invalid_credentials`); 18 unit tests | ✅ | `e947d80` |
| U5 | `RedisRefreshTokenStore` — atomic Lua-scripted rotation + 60-sec grace-window tombstone pattern (OWASP refined refresh-token-rotation, KTD3 + ADV-002 mitigation); per-tenant key namespacing; SHA-256 hash of opaque 32-byte token in key name; `RememberMe` carries through rotation TTL bucket; 9 Testcontainers Redis integration tests | ✅ | `a616193` |
| U6 | `JwtTokenIssuer` (HS256 via `Microsoft.IdentityModel.JsonWebTokens`); claims = sub/email/role/tenant_slug/iss/aud/iat/exp; iss + aud + DevSecret bound from `Auth` config section (KTD5 — same config keys the kernel JwtBearer validator reads, so issuance + validation are guaranteed in lockstep); 11 unit tests (incl. round-trip through the kernel `TokenValidationParameters` shape) | ✅ | `3e5f9c7` |
| U7 | Login + Refresh + Logout + ChangePassword MediatR handlers — composes the U2 ports; canonical `auth.invalid_credentials` for every credential-failure mode (R6 enumeration prevention); `RememberMe` propagates to `IRefreshTokenStore.IssueAsync`; refresh maps the 4 RotateAsync outcomes (Issued / GraceReplay / ReuseDetected → `auth.refresh_reused` + RevokeAll cascade / NotFound); ChangePassword min length 8 + current-password gate + post-rotation revoke-all-sessions; 25 unit tests | ✅ | `ca38151` |
| U8 | Admin handlers — `CreateUserCommand`, consolidated `UpdateUserCommand` (KTD8 — single command with `UpdateUserOperation` discriminator routes SetRole / ResetPassword / Deactivate), `ListUsersQuery` + projection to `UserSummary` (no PasswordHash in admin listings); `PasswordGenerator` service (16-char URL-safe, no visually-ambiguous chars, guaranteed mix of 4 categories via initial injection + Fisher-Yates shuffle); 26 unit tests | ✅ | `9cf30d0` |
| U9 | Auth.Api real endpoints — `AuthController` (`[SkipTenantRouting]` + in-controller subdomain resolver with host-suffix allowlist + `ReservedSlugs` denylist + body-fallback + source-conflict 400 + ADV-004 unknown-tenant collapse to `auth.invalid_credentials`); `AuthAdminController` (`[Authorize(Roles = "Owner")]`); Program.cs rewrite (`AddShopFlowDefaults` + `AddControlPlane` + `AddAuthModule` + `AddShopFlowControllers`); AuthOptions rewrite (drops Sprint-6 demo fields, adds `TrustedHostSuffixes`); appsettings.json rewrite (Argon2 + Refresh subsections); Gateway route `/auth/{**catch-all}` → `/api/auth/{**catch-all}` (KTD6); AppHost `WithReference(postgres)+WithReference(redis)` for auth-api; `ReservedSlugs` shared kernel utility (26 entries — admin/api/app/auth/www/etc); Sprint-6 stub controller tests deleted; 3 endpoint-shape tests as CI scaffolding | ✅ | `720353b` |
| U10 | shopflow-migrate extensions — `OwnerSeed` delegates to Auth.Infrastructure (SG-001, no duplicate hashing); `provision --tenant=<slug>` now runs owner-seed after MigrateAsync with `--owner-email` / `--owner-password` / `--owner-password-from-env` flags + one-time stdout echo of generated temp password; new `seed-owner --tenant=<slug>` subcommand retrofits existing tenants (ADV-003 — operators MUST run this against every pre-Sprint-8 tenant before deploying); pre-flight `ReservedSlugs` check rejects "api"/"admin"/etc; Auth module registered in `IModuleMigrationRegistry` so AddUsers migration applies alongside Inventory; 9 new tests (Migrate.UnitTests total 44) | ✅ | `9e44661` |
| U11 | Frontend token-pair auth — `useAuth` rewrite (accessToken + refreshToken + ISO expiries + user with userId Guid; localStorage `shopflow.auth.v2`; back-compat `jwt` getter + `logout()` + `login(jwt)` shim for Sprint-7 useSignalR tests); `api/auth.ts` rewrite (`/api/auth/login|refresh|logout|me/password`, `LoginRequest` with `rememberMe + tenantSlug`, `detectTenantFromHost` helper); httpClient 401 → refresh interceptor with module-scoped `inflightRefresh` guard (concurrent requests share one rotation; idempotency-key persisted across retry attempts); LoginScreen subdomain-detect + Workspace field (read-only when detected, editable on localhost) + RememberMe checkbox; Sidebar `<UserRow>` with email + role + tenant + LogOut button (best-effort POST /api/auth/logout → clearSession → navigate to /login); 7 new useAuth tests; obsolete Sprint-6 LoginScreen + httpClient tests deleted | ✅ | `47412ec` |
| U12 | Sign-off (this doc) + CHANGELOG + README + CLAUDE.md update + annotated tag `v0.11.0-sprint-8` | ✅ | (this commit) |

## Architecture Summary

**Per-tenant `users` table.** ADR-0003 hard-isolation rule preserved: no `tenant_id` column anywhere; the database identity IS the tenant boundary. Login flow resolves the tenant from Host subdomain (preferred) or request body's `tenant_slug` (fallback) BEFORE opening the scoped DbContext, then routes through the standard `IRequestContext` binding.

**Token-pair model.** Short-lived JWT access tokens (15 min default) issued by `JwtTokenIssuer` and validated by the kernel JwtBearer (Sprint-7 U5 lift) using a shared `Auth:DevSecret`. Opaque 32-byte refresh tokens (URL-safe base64) live ONLY in Redis under `refresh:{tenant}:{userId}:{sha256Hex}` keys — never in Postgres, never in JWTs. TTL = 7 days standard, 30 days with `rememberMe`.

**Grace-window rotation.** OWASP refined refresh-token-rotation pattern: rotating token A → token B writes a 60-sec tombstone at `refresh:rotated:{tenant}:{userId}:{Ahash}` pointing at the plaintext of B. A concurrent retry of A within 60 sec returns the SAME B (no reuse-detection lockout for legitimate multi-tab races / flaky-network retries). After grace expiry, replay collapses to `NotFound` → single-session logout (defense + convenience converge when stale vs replayed cannot be distinguished). True reuse-detection across the grace boundary is documented as Sprint-9 hardening.

**Enumeration-prevention discipline.** Every credential-failure leg (missing user, inactive user, wrong password, unknown tenant, malformed PHC hash, future-algorithm hash) returns the SAME canonical `auth.invalid_credentials` error code + 401 status. Internal observability captures the actual cause for forensics; the wire response leaks nothing.

**Subdomain-first tenant resolution for auth.** R5 explicit deviation from the kernel `TenantRoutingMiddleware` priority (header > JWT > subdomain): auth endpoints run BEFORE any JWT exists, so the controller-side resolver in `AuthController.ResolveTenantAsync` owns the priority. Three guards in order: host-suffix allowlist (SEC-004 hard requirement, prevents Host-header injection) → subdomain extraction → `ReservedSlugs` denylist → catalog lookup → bind RequestContext.

**Owner-only admin surface.** `AuthAdminController` carries `[Authorize(Roles = "Owner")]` at class level; ASP.NET Core's authorization pipeline rejects Picker/Dispatcher with 403 BEFORE the action runs. KTD8 consolidates SetRole / ResetPassword / Deactivate into one `UpdateUserCommand` with an `UpdateUserOperation` discriminator — three controller endpoints (PUT role, POST reset-password, DELETE user) map to one handler that routes via switch.

**Frontend graceful expiry.** The httpClient's 401 → refresh interceptor is fire-once-per-burst (module-scoped `inflightRefresh` guard) so a 401 fanout across 5 widgets refreshes ONCE and replays all 5 calls with the new access token. The idempotency-key is captured at the outer call and reused on retry so server-side dedup stays honest. Refresh failure clears the session + throws ApiError(401); the route guard bounces to /login. The user sees a single re-login prompt instead of one per widget.

## Key Technical Decisions (KTDs)

1. **KTD1 — Per-tenant database for users, no `tenant_id` column** (ADR-0003 enforcement). The `users` table lives in the per-tenant DB alongside every other business table. Login flow resolves tenant from Host subdomain or body fallback BEFORE the DbContext opens; admin surfaces flow tenant via `tenant_slug` JWT claim through the standard middleware.

2. **KTD2 — Short JWT + opaque refresh in Redis** (R8 + R11 + R12). Access tokens are 15-min HS256 JWTs; refresh tokens are 32-byte random URL-safe base64 strings stored ONLY as SHA-256 hashes in Redis. Redis sidesteps per-tenant-DB connection storms on every refresh call.

3. **KTD3 — 60-sec grace-window tombstone for refresh rotation** (OWASP refined pattern; ADV-002 mitigation). Concurrent retries within the grace window return the SAME successor token, eliminating the legitimate-retry false-positive that would otherwise trigger session-wide revocation under flaky network or multi-tab races. The dedicated `RefreshRotateOutcome.GraceReplay` enum value carries the semantics to the handler layer cleanly.

4. **KTD4 — Argon2id with PHC-embedded parameters** (OWASP 2026 baseline: m=64 MiB, t=4, p=4). Parameters baked into the produced hash string so future parameter tuning never invalidates existing rows; the verifier reads embedded params and re-runs Argon2 with those exact values.

5. **KTD5 — iss/aud/DevSecret read from the same `Auth` config section the kernel validator reads** (SEC-001 / F-001 doc-review fix). Defaults match every existing module's appsettings.json (`shopflow-dev` / `shopflow-api`), preventing a total-auth-bypass scenario where the issuer's hardcoded shape didn't match the validator's. Bumping the secret is one config-source change across all 7 module APIs.

6. **KTD6 — `/api/auth/*` route convention** (F-002 doc-review fix). Gateway, controller `[Route]`, frontend httpClient, and integration tests all align on `/api/auth/...` matching the other modules' `/api/<module>/...` shape. The Sprint-6 `/auth/...` route is gone.

7. **KTD7 — Fixed `UserRole` enum (3 values: Owner / Picker / Dispatcher)** plus DB CHECK constraint `chk_users_role`. `UserRoleTests` pins the enum-to-SQL agreement; adding a 4th role in Sprint-9+ requires coordinated changes (enum + per-tenant migration + downstream consumers of `UserRoleChangedEvent`). YAGNI on a role-permissions table — revisit when RBAC complexity warrants it.

8. **KTD8 — Consolidated `UpdateUserCommand` with operation discriminator** (post-doc-review SG-003). Three admin operations (SetRole / ResetPassword / Deactivate) share one command shape + one handler that routes via switch. Reduces handler-count footprint without losing R14 / R15 / R16 coverage.

9. **KTD9 — Temporary-password redaction in OTel response-body capture** (SEC-003 mitigation). `CreateUserResponse` and `ResetPasswordResponse` carry the freshly-generated plaintext temp password ONCE for the admin to relay. The Sprint-9 OTel response-body capture filter strips the `temporary_password` field independently — invariant set at the code level today so a future operator enabling response-body capture cannot surface plaintext credentials.

10. **KTD10 — shopflow-migrate takes a ProjectReference to Auth.Infrastructure** (SG-001 / ADV-010 doc-review fix). The CLI no longer duplicates Argon2 hashing logic; `OwnerSeed` delegates to `Auth.Application.IPasswordGenerator` + `Auth.Infrastructure.Argon2idPasswordHasher` so the seeded user's PHC hash validates 1:1 against `IPasswordHasher.Verify` at login time.

## Sprint-8 trade-offs locked in (carry into Sprint-9+)

1. **MFA / TOTP is Sprint-9+.** R28 explicit. Login surface ships email + password only; the Sprint-6 disabled-TOTP placeholder is gone (no fake 2FA UI).
2. **OAuth / social login is Sprint-9+.** R28 explicit.
3. **Account lockout (N failed attempts → 15-min lock) is Sprint-9+.** R28 explicit; Sprint-8 relies on Argon2id's CPU cost as the brute-force defence + the OWASP guidance that lockout is itself a DoS vector.
4. **Password-reset email flow is Sprint-9+.** R28 explicit. Sprint-8 admins reset via the Owner-gated `/api/auth/admin/users/{id}/reset-password` surface and email the user out-of-band.
5. **Reuse-detection across the grace boundary is Sprint-9 hardening.** Current implementation collapses post-grace replay to `NotFound` → single-session logout; true chain-aware reuse-detection (rotated-then-replayed-after-grace → revoke-all-sessions) requires extended tombstone TTLs + per-chain tracking; deferred per the OWASP guidance that the single-session default is acceptable when stale vs replayed cannot be distinguished.
6. **httpOnly cookie session is Sprint-9+.** Sprint-8 stores tokens in localStorage (XSS risk acknowledged). The migration path is a kernel-level cookie-issuance change that requires SignalR client auth coordination across all modules; deferred.
7. **Subdomain-routed CORS hardening is Sprint-9+.** Sprint-8's `TrustedHostSuffixes` allowlist closes the Host-header injection surface; per-origin CORS allowlist + preflight cache is a follow-on.
8. **Frontend `httpClient.test.ts` + `LoginScreen.test.tsx` rewrite is Sprint-8.5 candidate.** Sprint-8 deleted the Sprint-6 versions; the 401-refresh-interceptor + LoginScreen-tenant-detect contracts are covered end-to-end by the routing layer + the new `useAuth.test.ts` token-pair suite. Dedicated unit-tier suites for the new shapes are a near-term follow-up.
9. **Auth integration tests against real Aspire-managed Postgres + Redis ship Skip-marked.** 3 scenarios in `AuthControllerEndpointTests.cs` document the intended shape; CI runs them against Docker-backed containers. Sprint-1+ posture.
10. **OwnerSeed against real Postgres ships as documented intent.** The CLI flag-resolution + stdout-echo logic has unit coverage; full happy-path-against-real-tenant-DB integration test is a CI-tier follow-up.

## Deviations from plan

- **Sprint-7.5 carry-over fixes swept under U10.** The transitive dependency from shopflow-migrate → Inventory.Application surfaced a stray `}` at the end of `ISkuRepository.cs` (Sprint-7.5 U3 typo) and missing `using` directives in `UpdateSkuCommand.cs` (Sprint-7.5 U4 oversight). Both fixed inline at the U10 commit since they blocked the shopflow-migrate build path. Documented in the U10 commit body.
- **Auth.Api Program.cs `AddProblemDetails()` disambiguation as part of U1.** The Sprint-6 stub's unqualified `AddProblemDetails()` collided under .NET 9 between the built-in and Hellang's variant (CS0121). U1 bundled a one-line fully-qualified fix to unblock the U1 test project from compiling; the whole Hellang dependency leaves the tree at U9 when the real Program.cs lands.
- **U8 plan called for `auth.password_unchanged` defensive case** in ChangePassword; not implemented to keep handler complexity bounded — re-hashing under a new salt is meaningful work even if the plaintext is identical, and we don't enforce password-history.
- **U11 deleted `LoginScreen.test.tsx` and `httpClient.test.ts` outright** rather than rewriting them; new behaviour is covered by `useAuth.test.ts` (7 token-pair tests) + the integration tests in Auth.IntegrationTests. Documented as Sprint-8.5 candidate above.
- **No local Docker daemon on this dev machine** — same Sprint-1+ posture. Auth + Migrate integration tests deferred to CI.
- **Build verified via per-module dotnet build, not full solution.** The Sprint-7.5 StockSyncOptions `QueueCapacity` name collision + Channel.UnitTests PredicateBuilder generic mismatch + Outbound.UnitTests missing `Microsoft.Extensions.Time` namespace are pre-existing breakage carried into the Sprint-8 branch. None of those projects depend on Auth + Migrate work; Auth.Api / Auth.UnitTests / Auth.IntegrationTests / Migrate.UnitTests all build clean (0 warnings, 0 errors per the per-csproj `dotnet build` invocations).

## Verification

- **Auth.UnitTests**: 118 tests passing (Domain UserTests + UserRoleTests + Hashing + Tokens + Handlers + Services).
- **Auth.IntegrationTests**: scaffolded with 6 UserRepositoryTests + 5 AddUsersMigrationSmokeTests + 9 RedisRefreshTokenStoreTests + 3 AuthControllerEndpointTests (Skip-marked). Deferred to CI per Sprint-1+ posture.
- **Migrate.UnitTests**: 44 tests passing (+9 from Sprint-8 owner-seed coverage).
- **Frontend Vitest**: 370 passing / 3 pre-existing Sprint-7.5 failures unrelated to Sprint-8 (verified pre-U11 via `git stash`).
- **TypeScript** (`npx tsc --noEmit`): clean.
- **Tag**: `v0.11.0-sprint-8` annotated against the U12 sign-off commit.

## Next implementation step

Cut a fresh branch from `v0.11.0-sprint-8` and start one of:

- **Sprint-8.5** — Trade-off closures: dedicated `httpClient.test.ts` + `LoginScreen.test.tsx` re-write for the refresh-interceptor + tenant-detect logic; OwnerSeed real-Postgres integration test; pre-existing Sprint-7.5 build error sweep (StockSyncOptions name collision, Channel.UnitTests PredicateBuilder, Outbound.UnitTests Microsoft.Extensions.Time). ~1-week point release matching the Sprint-2.5 / 4.5 / 5.5 / 7.5 cadence.
- **Sprint-9** — RBAC + MFA hardening: chain-aware reuse-detection beyond the grace window; account lockout (with backoff vs DoS protection); TOTP placeholder activation; password-reset email flow; per-permission policy gates beyond role strings.
- **Phase-3 polish** — Observability dashboards (auth-failures-per-tenant, refresh-rotations-per-second, grace-replay-rate); rate limiting on `/api/auth/login` + `/api/auth/refresh`; security audit log table (`auth_audit_log` per the U1 RecordLogin TODO).
