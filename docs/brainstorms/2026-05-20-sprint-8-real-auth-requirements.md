---
date: 2026-05-20
topic: sprint-8-real-auth
---

# Sprint-8 — Real Authentication Module

## Summary

Sprint-8 retires the dev-mode baked JWT in favor of a real authentication service: per-tenant `users` table + Argon2id password hashing + 15min JWT access tokens + Redis-stored refresh tokens (7d default; 30d when "remember me" checked) with rotation + reuse detection + 3 fixed roles (Owner / Picker / Dispatcher) + admin user-management endpoints + Auth.Api as the centralized auth surface + subdomain-per-tenant routing as the canonical workspace URL pattern. **Backend-focused** — the only frontend change is the login page's subdomain detection + remember-me checkbox; no new role-gated UI surfaces this sprint.

---

## Problem Frame

Sprint-6 shipped the first frontend vertical slice with a deliberate placeholder for auth: a stub `Auth.Api` (4-csproj quartet) that returns a baked JWT for any non-empty (email, password) tuple, with `tenant_slug = yensaokhanhhoa` and `role = tenant_seller` hard-coded into the token. This unblocked Sprint-6 to ship the Inventory write surface end-to-end without an upstream auth dependency, but it was always known-to-be-replaced — captured as Sprint-6 trade-off #8.

Sprint-7 closed that trade-off **in spirit**: it lifted `AddJwtBearer` configuration into `AddShopFlowDefaults` (kernel-wide JWT validation; SignalR hub access-token query redaction included), so every module API + the SignalR hub validate tokens through the same code path. But the baked-JWT issuance path stayed in place — the token validates correctly because the dev-mode JWT is signed with the same kernel secret; nothing yet authenticates a real user.

Sprint-7.5 closed the remaining Sprint-6/7 trade-offs (cosmetic SKU schema, camelCase wire, URL search-params, cursor pagination, flash-sale dual-write, saga UNIQUE, SagaPipeline split) + a cross-module index audit. The system is now **production-ready, big-data-seed-ready** — except for the one remaining gap: there are no real users. The Owner role is the only role the codebase knows about, and even that's the dev-mode `tenant_seller` string, not a real role claim.

This blocks every Sprint-9+ feature that needs role-based access:
- Picker pick-list view (Sprint-9 candidate) needs a `Picker` role to gate against
- Ops Dispatcher orders triage (Sprint-9 candidate) needs a `Dispatcher` role
- Settings/Users admin UI (Sprint-9+) needs admin-only role gates
- Any portfolio demo that mentions "secure multi-tenant SaaS" needs real authentication, not a dev-mode shortcut

Sprint-8's job: **swap in real auth that's sturdy enough to demo + follow standard security**, without ballooning into MFA + self-service signup + audit log UI + email-verified flows (those land in Sprint-9+). The smallest viable shape that closes the gap and gives Sprint-9+ a real role-gate surface to ship on top of.

The subdomain-per-tenant URL pattern (`<slug>.shopflow.com`) ships in Sprint-8 alongside auth because the login page is the natural moment to establish "which workspace am I in?" — the Slack / Linear / Notion pattern. Doing it later means re-touching the login form + httpClient base URL configuration in every future surface that builds links.

---

## Actors

- A1. **Owner** (per-tenant role): the tenant administrator. Full access to every existing endpoint. Manages tenant users (create / set role / reset password) via new admin endpoints.
- A2. **Picker** (per-tenant role): warehouse floor staff who execute pick-pack-ship operations. Sprint-8 establishes the role string + JWT claim shape but does not ship a Picker-only endpoint yet (Sprint-9+).
- A3. **Dispatcher** (per-tenant role): order operations overseer. Same Sprint-8 posture as Picker — claim exists, no role-gated endpoint yet.
- A4. **Anonymous user** (pre-login): hits `/api/auth/login` with workspace + email + password OR navigates to `<slug>.shopflow.com/login` which pre-fills the workspace.
- A5. **`Auth.Api`** (module): owns issuance — `/api/auth/login`, `/api/auth/refresh`, `/api/auth/logout`, `/api/auth/me/password`, `/api/auth/admin/users` family. Reads + writes per-tenant `users` table; reads + writes Redis refresh-token store.
- A6. **Other module APIs** (Inventory / Outbound / Channel / StockSync / Inbound / Analytics): validate JWT via existing `AddShopFlowDefaults` configuration. Do not issue tokens. Will host role-gated endpoints in Sprint-9+; for Sprint-8 their existing `[Authorize]` class-level attributes stay as-is.
- A7. **`shopflow-migrate` CLI**: extended `provision <slug>` subcommand seeds a default Owner user with a generated password printed once to stdout during tenant provisioning.
- A8. **`TenantRoutingMiddleware`**: extended to honor subdomain Host header as a tenant source for unauthenticated endpoints (login, refresh) where no JWT claim exists yet.

---

## Key Flows

- F1. **Workspace-scoped login (subdomain path)**.
  - **Trigger:** Anonymous user navigates to `<slug>.shopflow.com/login`.
  - **Actors:** A4, A5, A8
  - **Steps:** Frontend detects subdomain from `window.location.hostname` → login page hides the workspace field + binds `tenant_slug` to the detected slug. User submits email + password + remember_me. `Auth.Api` reads tenant from Host header (subdomain) via `TenantRoutingMiddleware`, opens per-tenant DbContext, looks up `users` row by email LOWER, verifies password via Argon2id. On match: issues JWT (access 15min, refresh 7d or 30d if remember_me), writes hashed refresh into Redis, updates `users.last_login_at`. Returns `{ access_token, refresh_token, expires_in, user }`.
  - **Outcome:** User receives a valid token pair; frontend stores tokens + redirects to the user's role-appropriate landing.
  - **Covered by:** R1, R2, R5, R6, R7, R10, R13, R14
- F2. **Workspace-scoped login (explicit fallback path)**.
  - **Trigger:** Anonymous user navigates to `localhost:5173/login` (local dev) or any non-subdomain URL.
  - **Actors:** A4, A5
  - **Steps:** Login page shows the workspace field. User enters workspace + email + password + remember_me. Body carries `tenant_slug`; `TenantRoutingMiddleware` resolves tenant from body. Rest identical to F1.
  - **Outcome:** Same as F1; local-dev story works without hosts-file edits.
  - **Covered by:** R1, R6, R10, R14
- F3. **Token refresh**.
  - **Trigger:** Frontend hits 401 on a module API call; httpClient interceptor calls `/api/auth/refresh` with the stored refresh token.
  - **Actors:** A5, A8
  - **Steps:** `Auth.Api` resolves tenant from subdomain Host or stored `tenant_slug` claim on the (about-to-expire) refresh token. Verifies token-hash exists in Redis. If found: rotates — issues new access + refresh pair, writes new hash, deletes old. If the presented refresh token doesn't exist in Redis AND was issued (suggesting reuse): revokes ALL active sessions for that user_id (deletes every Redis key matching `refresh:{tenant_slug}:{user_id}:*`) + returns 401.
  - **Outcome:** Valid refresh → new pair. Reused refresh → user logged out everywhere.
  - **Covered by:** R8, R9
- F4. **Logout**.
  - **Trigger:** User clicks Logout in the UI.
  - **Actors:** A5
  - **Steps:** Frontend calls `/api/auth/logout` with the access token. Backend deletes the bound refresh token's hash from Redis. Returns 204. Frontend clears local tokens + redirects to login.
  - **Outcome:** That session is terminated; other sessions for the same user (e.g., another browser) remain valid.
  - **Covered by:** R11
- F5. **Admin creates a tenant user**.
  - **Trigger:** Owner submits the admin "Create user" call (via API today; Sprint-9+ ships the UI).
  - **Actors:** A1, A5
  - **Steps:** Owner POSTs `/api/auth/admin/users` with `{ email, role }`. `Auth.Api` generates a 16-char random password, hashes with Argon2id, inserts row into per-tenant `users` table, returns `{ id, email, role, temporary_password }` ONCE in the response body (only response that ever surfaces a password). Owner shares password with the new user out-of-band.
  - **Outcome:** New user can log in with the temporary password. Password change is recommended but not enforced by Sprint-8 (an enforced first-time change is Sprint-9+).
  - **Covered by:** R15, R16, R17
- F6. **Tenant provisioning bootstrap**.
  - **Trigger:** Operator runs `shopflow-migrate provision <slug>` to add a new tenant.
  - **Actors:** A7
  - **Steps:** Existing provision flow (create tenant DB + apply migrations) extended to ALSO insert a default Owner user with email `owner@<slug>.local`, role `Owner`, generated 16-char random password (Argon2id-hashed). CLI prints the email + plaintext password ONCE to stdout. Operator shares with the tenant's bootstrap owner.
  - **Outcome:** Every freshly provisioned tenant has a single Owner who can log in immediately + create other users via admin endpoints.
  - **Covered by:** R18, R19
- F7. **Refresh-token reuse detection**.
  - **Trigger:** A stolen refresh token is used by an attacker after the legitimate user has already refreshed (rotated the token).
  - **Actors:** A5
  - **Steps:** Attacker's request hits `/api/auth/refresh`. The presented hash is no longer in Redis (rotation deleted it). `Auth.Api` recognizes this as reuse → revokes ALL active refreshes for the user_id (deletes every matching Redis key) + returns 401. Legitimate user is also logged out everywhere at next 401 (forced re-login).
  - **Outcome:** Stolen token cannot be used; legitimate user re-authenticates; attacker is locked out.
  - **Covered by:** R9, R20

---

## Requirements

**Workspace + login UX**
- R1. The login form accepts `tenant_slug` (workspace), `email`, `password`, and an optional `remember_me` boolean.
- R2. When the frontend is loaded from a subdomain matching `<slug>.shopflow.com` or `<slug>.localhost`, the login page detects the slug from the hostname, hides the workspace field, and uses the detected slug as the tenant.
- R3. When loaded from a non-subdomain host (e.g., `localhost:5173`, IP, direct domain without slug), the workspace field is visible and required.
- R4. The frontend httpClient base URL derives from the current hostname so module API calls inherit the subdomain — and falls back to an env-configured base URL for local dev without subdomain hosts entries.

**Tenant routing**
- R5. `TenantRoutingMiddleware` resolves the per-request tenant from the following sources, in priority order: (a) subdomain segment of the Host header, (b) explicit `X-Tenant-Slug` request header, (c) `tenant_slug` claim on the validated JWT, (d) `tenant_slug` field in the request body — but ONLY for the auth-endpoint allow-list (login, refresh). When two or more sources are present and disagree, the request is rejected 400 with a stable error code `tenant.source_conflict` and the disagreement is recorded in audit logs / OpenTelemetry traces.

**Authentication endpoints**
- R6. `POST /api/auth/login` accepts `{ tenant_slug?, email, password, remember_me? }`. Returns `200 { access_token, refresh_token, expires_in, user: { id, email, role } }` on success or `401 { code: "auth.invalid_credentials" }` on bad credentials, missing user, or inactive user. `tenant_slug` is omitted from the body when the subdomain supplied it.
- R7. The JWT access token is signed with the kernel HS256 secret, carries `sub=user_id`, `tenant_slug`, `role`, `email`, `iat`, `exp` (15-min TTL), `iss=shopflow-wms`, `aud=shopflow-modules`. The refresh token is opaque (random 256-bit hex) — NOT a JWT.
- R8. `POST /api/auth/refresh` accepts `{ refresh_token }`. Returns a new `{ access_token, refresh_token, expires_in }` pair on success or `401` on invalid / expired / unknown / reused token.
- R9. Every refresh rotates: the old refresh hash is deleted from Redis and a new one is written in the same operation. Presenting a refresh token whose hash is NOT in Redis but WAS previously valid (reuse detection) triggers revocation of ALL active refresh tokens for that user_id.
- R10. `POST /api/auth/logout` accepts an `Authorization: Bearer <access_token>` header and the matching `refresh_token` in body. Backend removes that specific refresh token's hash from Redis. Returns 204. Other sessions for the same user remain valid.
- R11. `POST /api/auth/me/password` accepts `{ current_password, new_password }`. Verifies current via Argon2id, validates new (min 8 chars), updates `users.password_hash`. Returns 204. All other refresh tokens for the user are revoked (force re-login on other sessions); the current session's refresh stays valid.

**Admin endpoints (Owner role only)**
- R12. `POST /api/auth/admin/users` accepts `{ email, role }`. Generates 16-char random password, hashes via Argon2id, inserts new `users` row, returns `201 { id, email, role, temporary_password }` exposing the plaintext password ONCE — never accessible again.
- R13. `GET /api/auth/admin/users` returns the tenant's users `[{ id, email, role, is_active, created_at, last_login_at }]`. Owner-only.
- R14. `PUT /api/auth/admin/users/{userId}/role` accepts `{ role }`. Updates `users.role`. Active sessions for the affected user keep their existing access token until expiry (15min max); the role claim updates on next refresh. Returns 204.
- R15. `POST /api/auth/admin/users/{userId}/reset-password` regenerates a 16-char password, updates the hash, returns `{ temporary_password }` ONCE. Revokes ALL active refresh tokens for that user.
- R16. `DELETE /api/auth/admin/users/{userId}` sets `is_active = false` and revokes all refresh tokens. Soft-delete to preserve audit references; row is retained.

**Password storage**
- R17. Passwords are hashed via Argon2id (`Konscious.Security.Cryptography.Argon2` NuGet). Parameters tunable via `appsettings` (default: 4 iterations, 64MB memory, 4 parallelism — OWASP 2026 baseline). Plaintext passwords never appear in logs, traces, or stored fields.
- R18. Password minimum length 8 chars; no complexity rules enforced this sprint (defer to Sprint-9+).

**Refresh token storage**
- R19. Refresh tokens are stored hashed (SHA-256) in Redis with key `refresh:{tenant_slug}:{user_id}:{token_hash}` and JSON value `{ user_id, issued_at, expires_at, remember_me }`. Redis-native TTL set to the remaining lifetime so expired tokens auto-evict.
- R20. Default refresh TTL is 7 days; when login carries `remember_me: true`, TTL is 30 days. Rotation preserves the original TTL bucket (the new refresh issued via /refresh inherits the original session's TTL bucket — a 30d "remember me" session continues to rotate as 30d tokens; a 7d session continues as 7d).

**Roles**
- R21. Three fixed roles enum string values: `Owner`, `Picker`, `Dispatcher`. Implemented as a string column on `users.role` with a DB-level CHECK constraint. Single role per user this sprint (no multi-role assignments).
- R22. The JWT `role` claim carries the user's current role string. Module APIs gate endpoints via `[Authorize(Roles = "Owner")]` (and future Picker/Dispatcher attributes). For Sprint-8 the existing 5 class-level `[Authorize]` attributes are unchanged — they accept any authenticated user, which during Sprint-8 means anyone with a valid tenant JWT.

**Schema**
- R23. New per-tenant `users` table: `id GUID PRIMARY KEY, email TEXT NOT NULL, password_hash TEXT NOT NULL, role TEXT NOT NULL CHECK(role IN ('Owner', 'Picker', 'Dispatcher')), is_active BOOLEAN NOT NULL DEFAULT TRUE, created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(), updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW(), last_login_at TIMESTAMPTZ NULL`.
- R24. Unique index on `LOWER(email)` for case-insensitive email lookup. No partial index — every active user must have a unique email.
- R25. The `users` table is the FIRST identity-bearing aggregate in the tenant DB; the rest (Sku, Reservation, Order, etc.) reference it only via the JWT claim's `sub`, not via FK.

**Tenant provisioning bootstrap**
- R26. `shopflow-migrate provision <slug>` is extended to seed one default Owner user (`email = owner@<slug>.local`) after applying the tenant schema migrations. Password is generated random 16-char, hashed via Argon2id, written to the `users` table, and printed ONCE to stdout as a one-line credential summary the operator copies into a secure share-out channel.
- R27. The provision command supports `--owner-email <addr>` and `--owner-password <plaintext>` overrides for non-default bootstrap; both are optional and default to the auto-generated values.

**Auth.Api retirement**
- R28. The Sprint-6 stub `AuthController` returning a baked JWT is deleted in the same commit as the real implementation lands. No parallel-existence period. The dev-mode `tenant_seller` role string is retired.

**Operational + Frontend changes**
- R29. The frontend's stored token format moves from "single JWT in localStorage" to "access + refresh tokens in localStorage". The httpClient interceptor refreshes on 401 transparently (within a single pending-request lock so concurrent calls don't trigger multiple refreshes).
- R30. The login page surface gains a "Remember me" checkbox (defaults unchecked — opt-in to 30d session). The workspace field renders only when no subdomain was detected.
- R31. Logout button surfaces in the existing Sidebar component (likely under the user-display row). Clicks call `/api/auth/logout` + clear local tokens + redirect to login.

---

## Acceptance Examples

- AE1. **Covers R2, R6, R20.** Given the user navigates to `https://yensaokhanhhoa.shopflow.com/login`, when the page loads, the workspace input is hidden + the form pre-fills `tenant_slug=yensaokhanhhoa` from the hostname; the user enters their email + password + checks "Remember me"; on submit the API returns a token pair where the refresh-token TTL is 30 days (verified via Redis key TTL inspection on the test environment).
- AE2. **Covers R5.** Given a request to `POST /api/auth/login` carrying both Host header `yensaokhanhhoa.shopflow.com` AND body field `tenant_slug=otherco`, when the middleware processes the request, it returns 400 with `code: "tenant.source_conflict"` + records the disagreement in OpenTelemetry traces.
- AE3. **Covers R9.** Given a user has logged in (refresh `R1` stored in Redis), then refreshed (R1 deleted, R2 stored), when an attacker presents the stolen original `R1` to `/api/auth/refresh`, the response is 401 + every refresh key `refresh:{tenant_slug}:{user_id}:*` for that user is deleted from Redis (verified by Redis SCAN); the legitimate user's `R2`-bearing session terminates at next /refresh attempt.
- AE4. **Covers R17, R23.** Given an existing Sprint-7.5 test database, when the Sprint-8 migration runs, the `users` table exists with the documented schema and CHECK constraint; attempting to insert a row with `role='Admin'` fails (the constraint enumerates only `Owner / Picker / Dispatcher`).
- AE5. **Covers R12, R15.** Given the Owner POSTs `/api/auth/admin/users` with `{ email: "picker1@example.com", role: "Picker" }`, the response is 201 with `temporary_password` field present; the new Picker can immediately log in with the temp password; a subsequent admin reset-password call rotates the password + revokes all refresh tokens for that Picker.
- AE6. **Covers R26.** Given the operator runs `shopflow-migrate provision newtenant`, when the command completes, the per-tenant `users` table contains exactly one Owner row with `email=owner@newtenant.local`; stdout includes a single credential-summary line like `Created owner@newtenant.local — temporary password: aB3xK9...` (16-char random); the password is NOT also written to any log file or database column other than the hash.
- AE7. **Covers R28, R7.** Given the Sprint-6 stub `AuthController` was returning a baked JWT with `role=tenant_seller`, when Sprint-8 ships, the stub is deleted; a `grep` for `tenant_seller` returns no hits in `src/`; any test or code path that referenced the dev-mode JWT issuance is updated to use real login or fails closed.

---

## Success Criteria

- All 31 requirements implemented; AE1-AE7 covered by automated tests where mechanically verifiable (AE6 is a CLI test; AE2-AE5 are integration tests; AE7 is a verification step at commit time).
- Sprint-8 closes Sprint-6 trade-off #8 (real auth) properly. The dev-mode baked-JWT path is gone from the codebase.
- Sprint-9+ has a real `Owner` / `Picker` / `Dispatcher` role gate to ship its first multi-role UI surface on top of, without re-touching the auth surface.
- Auth admin endpoints support the Sprint-9+ "Settings / Users" UI without backend changes (the UI lands later; the API is ready).
- Backend test count grows by an appropriate amount (~30-40 new tests covering Auth.Domain + Auth.Application + Auth.Infrastructure + the new endpoint behaviors + the OWASP-recommended rotation + reuse-detection scenarios).
- Frontend test count grows by a smaller amount (~10-15 new tests covering login page subdomain detection + remember-me flow + httpClient base URL derivation + 401 refresh interceptor).
- Subdomain-per-tenant routing works end-to-end against a local hosts-file entry (`yensaokhanhhoa.localhost`) so the dev demo doesn't require a real wildcard DNS / TLS cert.
- Tagged `v0.11.0-sprint-8` + sign-off doc + CHANGELOG entry + README + CLAUDE.md update.
- For `ce-plan`: implementation can pick up the unit list, schema decisions, and architectural choices without re-litigating product behavior.

---

## Scope Boundaries

Sprint-8 deliberately excludes:

- **MFA (TOTP enrollment + verify)** — Sprint-9+.
- **Self-service signup** — Sprint-9+. Admin endpoints seed users for now.
- **Email-verified password reset (forgot-password flow)** — Sprint-9+; requires email service infrastructure.
- **Audit log UI** — Sprint-9+. Sprint-8 auth events surface in OpenTelemetry traces only.
- **Session management UI** in Settings ("List active sessions, revoke this one") — Sprint-9+.
- **New role-gated UI surfaces** (Settings/Users admin, Picker pick-list, Dispatcher triage) — Sprint-9+. Sprint-8 establishes the role claims + admin API; UI lands later.
- **Email service infrastructure** — passwords are surfaced in API response / CLI stdout; never emailed.
- **OAuth / SSO / WebAuthn / passkeys** — Sprint-10+ if at all.
- **Account lockout after N failed login attempts** — Sprint-9+. Redis-backed counter is cheap to add when needed.
- **Password complexity beyond min-length** — Sprint-9+.
- **DNS wildcard + TLS wildcard certificate provisioning** — operational concern. Sprint-8 code accommodates the subdomain pattern but ops owns the cert + DNS in any real deployment. Local dev uses `<slug>.localhost` hosts entries.
- **Subdomain typo protection / rate limiting on unknown-tenant attempts** — Sprint-9+ (Redis-backed counter pattern). For Sprint-8, an unknown subdomain returns 404 from the catalog lookup; no special rate-limiting.
- **Multi-role-per-user** — single role this sprint. Add a `user_roles` junction table when a 5th role lands or a real RBAC matrix surfaces.
- **Cross-tenant admin** (one user managing N tenants) — explicitly out of identity model.
- **Enforced first-time password change** — Sprint-9+. Sprint-8 issues temp passwords + recommends change but doesn't enforce.
- **Backwards-compatible dev-mode JWT escape hatch** — hard cut. Local dev uses real Owner credentials seeded via `shopflow-migrate provision`.

---

## Key Decisions

- **Auth.Api stays as the centralized auth service** in the modular monolith. All other module APIs validate via `AddShopFlowDefaults` (Sprint-7 KTD6); only Auth.Api issues. After the W6 mechanical split, nothing changes architecturally — Auth.Api just runs in its own process.
- **Per-tenant `users` table**, not a shared catalog users table. Matches ADR-0003 (DB-per-tenant for PDPA hard isolation). Right-to-erasure is `DROP DATABASE` — automatic. Login form takes explicit `tenant_slug` for local dev; subdomain routing carries it implicitly in production.
- **Subdomain-per-tenant routing as the canonical workspace URL** — `<slug>.shopflow.com` is the entry point. Standard B2B SaaS pattern (Slack, Linear, Notion). Login page detects + pre-fills tenant; httpClient base URL derives from hostname; `TenantRoutingMiddleware` honors Host header as tenant source. Local-dev fallback path preserved (explicit body / hosts-file entries).
- **Argon2id for password hashing** (`Konscious.Security.Cryptography.Argon2` NuGet) — OWASP-recommended 2026 default; tunable parameters (4 iter / 64MB / 4 par baseline).
- **JWT access (15min) + opaque-hex refresh tokens (7d default / 30d remember-me)** — refresh tokens are NOT JWTs; just 256-bit random hex. Storage: hashed in Redis with native TTL.
- **Token rotation per use + reuse detection revokes all sessions** — OWASP refresh-token-rotation pattern. A stolen-then-reused refresh logs out the legitimate user too (defense > convenience).
- **3 fixed roles enum** (`Owner` / `Picker` / `Dispatcher`) — single string column on `users.role` with DB-level CHECK constraint. YAGNI on a role-permissions table; revisit when a 5th role lands or RBAC matrix becomes real.
- **"Remember me" extends refresh TTL only** — no extended access-token TTL, no separate "long-lived" refresh tier. 7d default → 30d when remember_me=true. Rotation preserves the TTL bucket across refreshes.
- **Admin bootstrap via `shopflow-migrate provision`** — provisioning a tenant DB also seeds one Owner. CLI prints the temp password ONCE; nothing else logs or stores it. Mirrors the Phase-0-redux pattern of putting tenant-lifecycle operations in the CLI.
- **No email service** — passwords are returned in API response body (admin create / admin reset) or printed to CLI stdout (provision). All forgot-password flows defer until email infra lands.
- **The dev-mode baked JWT is hard-cut, not deprecated** — no escape hatch. Local dev uses real Owner credentials from the provision step.
- **Existing 5 `[Authorize]` attributes on Inventory + Outbound + TenantHub stay as-is** in Sprint-8. They're "any authenticated user in the tenant." Role-specific gates land in Sprint-9+ when the first role-gated UI surface ships.
- **Token storage on the frontend stays localStorage** — same as Sprint-6's baked-JWT pattern. Future hardening (httpOnly cookies, BFF token-proxy) is Sprint-10+ if portfolio narrative needs it.
- **Auth events surface in OpenTelemetry traces only** for Sprint-8. A dedicated `auth_audit_log` table is Sprint-9+ when audit UI surfaces.

---

## Dependencies / Assumptions

- Parent tag is `v0.10.1-sprint-7.5`; branch cut from there.
- ADR-0003 (database-per-tenant) is the foundational architectural constraint and stays unchanged.
- `AddShopFlowDefaults` JWT validation configuration (Sprint-7 KTD6) stays as-is — Sprint-8 only adds new issuance behavior in `Auth.Api`; validation is unchanged.
- Redis is provisioned in Aspire (Phase-0-redux U7) and reachable from Auth.Api with appropriate connection-string configuration.
- The `shopflow-migrate` CLI (Phase-0-redux U6) is extended with one new subcommand parameter set; no new CLI primitives required.
- `TenantRoutingMiddleware` (Phase-0-redux U4) has documented hooks for adding tenant sources; subdomain Host parsing is a new source added to the existing priority chain.
- Argon2id library `Konscious.Security.Cryptography.Argon2` is an established .NET NuGet (>5 years, OWASP-cited). No alternative library required.
- Local dev assumes the developer can add `<slug>.localhost` entries to their hosts file OR uses the `localhost:5173` + explicit-body-tenant path.
- The TLS wildcard cert + DNS wildcard for `*.shopflow.com` are operational concerns; production deploy doc lists them as prerequisites. Sprint-8 ships the code path that uses subdomain routing — ops wires up the production DNS / cert separately.
- Big-data seed loader (the workstream Sprint-7.5's production-readiness posture was sized for) is independent of Sprint-8; can run on either branch.
- Frontend stays on Vite 5 + React 19 + TypeScript strict + TanStack Router + TanStack Query (Sprint-6/7 stack unchanged).

---

## Outstanding Questions

### Resolve Before Planning

*(none — synthesis covered every scope-shaping decision)*

### Deferred to Implementation

- `[Affects R7][Technical]` HS256 vs RS256 signing key — Sprint-7 used HS256 with the kernel `Auth:DevSecret`. Sprint-8 keeps HS256 for simplicity (single-secret, no key rotation infrastructure). RS256 + JWKS would enable token verification by third parties / mobile clients but adds key-management infra; defer unless the multi-process W6 split forces it.
- `[Affects R17][Technical]` Argon2id tuning parameter persistence — store the params as part of the hash string (modular format) so the value can be reseated without breaking existing hashes. `Konscious` library supports this natively; ce-plan picks the exact encoding.
- `[Affects R20][Technical]` "Remember-me bucket preservation across rotation" — refresh-token rotation preserves the original TTL bucket. Implementation needs to read `remember_me` from the stored value AND keep it on the new value. ce-plan picks how that's wired (carry in the Redis JSON or recompute from the original `expires_at`).
- `[Affects R5][Technical]` Subdomain extraction regex / strict-match rule — `<slug>.shopflow.com` vs `<slug>.shopflow.localhost` vs `<slug>.localhost` (local dev). ce-plan defines the host-match patterns from a configurable allowlist.
- `[Affects R30][Frontend]` Login page redesign vs minimal edit — Sprint-6's existing login form layout. ce-plan reads the current shape + decides "extend in place" vs "rewrite as workspace-first."
- `[Affects R29][Frontend]` Pending-refresh request locking — multiple in-flight calls hit 401 simultaneously; only one should fire `/refresh`; the others wait + retry once the new token lands. ce-plan picks the implementation (lock + queue, debounce, or library — `axios-auth-refresh` if axios is in use).
- `[Affects R26][Operational]` `shopflow-migrate provision` JSON output mode — currently a plain-text CLI. ce-plan decides if the temp-password output goes to stdout only or also to a `--output-file` for automation. Don't write plaintext to a JSON log file by default.
