---
date: 2026-05-20
topic: sprint-9-rbac-mfa-hardening
---

# Sprint-9 — RBAC + MFA Hardening (with ShopFlow.Notification)

## Summary

Sprint-9 closes five Sprint-8 deferred trade-offs as a single RBAC + MFA hardening bundle and lands the 7th business module — `ShopFlow.Notification` — to host outbox-routed transactional email and Owner-targeted security alerts. Backend + auth-frontend only; no new Picker/Dispatcher feature surfaces this sprint, just the per-permission gating infrastructure that Sprint-10+ feature work will sit on top of.

---

## Problem Frame

Sprint-8 retired the dev-mode baked JWT and shipped real authentication — Argon2id, JWT + refresh tokens with 60-sec grace-window rotation, per-tenant `users` table, Owner-only admin surface, subdomain-first tenant resolution. The sign-off explicitly locked in eight trade-offs to carry forward; five of them cluster under "RBAC + MFA hardening" because together they form the productionalisation envelope around the Sprint-8 token-pair. Each one alone is portfolio-grade incomplete:

- Grace-window rotation collapses post-grace replay to single-session logout. There is no chain awareness, no per-chain revocation, no operator visibility on suspicious activity. Real production auth catches reuse across the chain and revokes; OWASP RFC describes exactly this pattern and Sprint-8 documented the gap as Sprint-9 hardening.
- Argon2id CPU cost is the only brute-force defence today. A patient attacker can spray dictionary credentials against any known email address without ever tripping a lockout. Account-targeted attacks and credential-stuffing sprays both close cleanly under per-account lockout + per-IP rate limit.
- The frontend has no second factor. Owner accounts — the highest-value tenant target in the system — are guarded by password alone. Picker/Dispatcher MFA is optional friction; Owner MFA is table stakes for any portfolio demo that mentions "secure multi-tenant SaaS".
- Password reset is admin-only out-of-band today (Sprint-8 F5 / R15 — Owner creates the user with a temp password and emails it out-of-band). Users cannot self-recover. Every forgotten-password event blocks on Owner time and on a side-channel email the system itself does not own.
- Authorization today is authenticated-user-equality. Every module endpoint carries plain `[Authorize]` (any authenticated user passes); only `AuthAdminController` carries `[Authorize(Roles = "Owner")]`. There is no permission layer and no per-endpoint role gate on the business modules; any Sprint-10+ Picker-only pick-list view or Dispatcher-only ops dashboard would have to invent the per-permission pattern from scratch.

Plus, no module today owns transactional email or operator alerts. Sprint-9's password-reset email + chain-reuse Owner notification create the natural moment to land `ShopFlow.Notification` as the 7th business module — outbox-consuming, MassTransit-routed, dev-mode mock-SMTP, prod-mode SMTP-provider-pluggable. Sprint-10+ event-driven notifications (MFA enrolled confirmation, order milestone alerts, channel webhook anomalies) sit on top of this foundation; not laying it now means re-touching every consumer when the first cross-cutting email need lands.

The cost of NOT shipping Sprint-9: Owners log in with password alone; brute-force-against-portfolio-demo is a live risk; chain-aware reuse-detection is the OWASP-canon thing the system promises but does not yet deliver; per-permission policy is the gate every Sprint-10+ role-specific feature needs.

---

## Actors

- A1. **Owner**: role-required to enroll TOTP at first post-Sprint-9 login; receives chain-reuse + account-lockout email alerts; manages tenant users, MFA status, locked-account unlocks, and role→permission assignments via admin surface.
- A2. **Picker / Dispatcher**: may optionally self-enroll TOTP via profile menu; subject to account lockout + per-IP rate limit + per-permission gating; cannot receive admin alerts.
- A3. **Anonymous user (pre-login)**: hits `/api/auth/login` (subject to per-IP rate limit + per-account lockout) or `/api/auth/forgot-password` (subject to per-account cooldown).
- A4. **Auth.Api**: owns issuance — login + refresh + MFA challenge + MFA verify + MFA enroll + forgot-password + reset-confirm; writes lockout state; writes auth_audit_log; emits notification events to its outbox.
- A5. **ShopFlow.Notification (NEW)**: 7th business module; consumes Auth's outbox events via MassTransit; renders templated email; sinks to a dev-mode mock SMTP container or a prod-mode pluggable SMTP provider behind `IMailerProvider`.
- A6. **Other module APIs (Inventory / Outbound / Channel / StockSync / Inbound / Analytics)**: gate endpoints on per-permission policies (replaces the existing role-only `[Authorize(Roles = "Owner")]`); read permission claims from JWT, no per-request DB lookup.
- A7. **Frontend client**: new MFA enrollment + challenge + recovery-codes UI; new forgot-password + reset-confirm screens; profile menu adds 2FA management; Owner admin gains MFA-status column + locked-accounts panel + role→permissions editor.
- A8. **shopflow-migrate CLI**: extended to apply Notification module migrations against newly-provisioned tenants and seed the default `role_permissions` catalog (Owner = all, Picker/Dispatcher = empty).

---

## Key Flows

- F1. **Login with no MFA (Picker/Dispatcher, opt-in not taken)**
  - **Trigger:** A2 or A3 POSTs `/api/auth/login` with workspace + email + password.
  - **Actors:** A2 / A3, A4
  - **Steps:** Per-IP rate limit checked (10/min token bucket). Per-account lockout checked (`locked_until > now` → silent `auth.invalid_credentials` 401). Argon2id verify. On match: `failed_login_count = 0`, JWT issued with permission claims projected from user.role, refresh token written to Redis with `chain_id`. On password mismatch: `failed_login_count++`; if it hits 5 within 15 min → `locked_until = now + 15min` and emit `AccountLockedV1` to outbox; always return `auth.invalid_credentials` 401 (R6 enumeration prevention).
  - **Outcome:** Token pair issued OR collapsed 401. auth_audit_log captures the actual cause internally.
  - **Covered by:** R5, R18, R19, R20, R22, R23

- F2. **Login with MFA (Owner default; opted-in Picker/Dispatcher)**
  - **Trigger:** Same as F1 but for a user with `mfa_enrolled = true`.
  - **Actors:** A1 (or A2 opted-in), A4
  - **Steps:** Same per-IP + lockout + Argon2id verify. On password success, instead of issuing a token pair, issue a short-lived `mfa_challenge_token` (5-min JWT, sole purpose: MFA verify). Frontend prompts for 6-digit OTP. User POSTs `/api/auth/mfa/verify` with challenge token + OTP. Server validates the challenge token + verifies OTP against the user's encrypted TOTP secret (`OtpNet`-style 30-sec window, ±1 step drift). On match: issue real access + refresh pair. On OTP mismatch: collapse to `auth.invalid_credentials` 401, increment `failed_login_count` (MFA failures count toward lockout).
  - **Outcome:** Token pair issued only after both factors verify.
  - **Covered by:** R8, R10, R12, R13, R19

- F3. **First-time Owner login → forced MFA enrollment**
  - **Trigger:** Owner with `mfa_required = true` AND `mfa_enrolled = false` POSTs `/api/auth/login`.
  - **Actors:** A1, A4, A7
  - **Steps:** Password-verify succeeds → server returns a single-purpose `mfa_enrollment_required` short-lived token instead of a normal token pair. Frontend forces redirect to /mfa/enroll. User clicks "Begin enrollment" → POST `/api/auth/mfa/enroll/begin` with the enrollment token → returns QR-provisioning URI + manual secret + a transactional UUID. User scans QR with authenticator app → enters first OTP → POST `/api/auth/mfa/enroll/verify` with UUID + OTP. On match: persist secret + generate 10 recovery codes (Argon2id-hashed, plaintext displayed ONCE), set `mfa_enrolled = true`, emit `MfaEnrolledV1`, return real token pair. User must acknowledge having saved recovery codes before continuing.
  - **Outcome:** Owner cannot reach any other route until enrollment completes.
  - **Covered by:** R10, R11, R12, R15, R36

- F4. **Self-service password reset**
  - **Trigger:** Anonymous user clicks "Forgot password?" on login screen.
  - **Actors:** A3, A4, A5, A7
  - **Steps:** User submits workspace + email. POST `/api/auth/forgot-password` always returns 200 (R6 silent). Server checks per-account cooldown (max 1/5min/email); if not in cooldown AND user exists, generates 32-byte URL-safe token, persists SHA-256 hash with 30-min TTL, emits `PasswordResetRequestedV1` to outbox with plaintext token + user.email + workspace. Notification module consumes, renders email with deep link `<workspace-host>/reset-password?token=<plaintext>`, sinks to mailer (Mailcatcher in dev, real SMTP in prod). User clicks link → reset-password screen → enters new password → POST `/api/auth/reset-password/confirm` with token + new_password. Server validates token-hash + not used + not expired → resets password via Argon2id → marks token used → revokes ALL refresh tokens for user (kicks every active session as a security event) → 204. Frontend redirects to /login.
  - **Outcome:** User regains access; all other sessions invalidated.
  - **Covered by:** R29, R30, R31, R32, R33

- F5. **Chain-reuse detection → revoke-chain + Owner email**
  - **Trigger:** A presented refresh token's tombstone exists AND grace window expired.
  - **Actors:** A4, A5
  - **Steps:** Refresh handler probes Redis: live record missing, tombstone present (extended TTL = 7d), `now - rotated_at > 60sec`. Read `chain_id` from tombstone payload. DEL all refresh-token records with that chain_id (kicks just that browser session, not other devices). Emit `RefreshReuseDetectedV1` to outbox with chain_id + user_id + presented_token_hash + presenting_ip + user_agent + UTC timestamp. Return `auth.refresh_reused` 401 to the caller. Notification module consumes, renders Owner alert email "Suspicious activity on account {email}: a previously-rotated session token was replayed from IP {ip} at {time} UTC. That session has been signed out. If this wasn't you, change your password immediately."
  - **Outcome:** Just the affected chain revoked; Owner has visibility within minutes.
  - **Covered by:** R23, R24, R25, R26, R27, R28

- F6. **Module endpoint authorization via permission claim**
  - **Trigger:** Authenticated user calls any non-anonymous module endpoint (e.g., `POST /api/inventory/adjust`).
  - **Actors:** A2, A6
  - **Steps:** Kernel JwtBearer validates the JWT. ASP.NET Core authorization pipeline matches the endpoint's `[Authorize(Policy = "inventory.adjust")]` attribute. Policy handler reads the `perm` array claim from the validated principal. If `inventory.adjust` is in `perm`: pass; controller action runs. If not: 403 (NOT collapsed 401 — authorization failure is meaningfully distinct from authentication failure and not enumeration-sensitive).
  - **Outcome:** Picker without inventory.adjust permission gets 403; Owner with all permissions gets 200.
  - **Covered by:** R2, R3, R4, R5, R6

---

## Requirements

**Per-permission RBAC**
- R1. Existing fixed `UserRole` enum (Owner / Picker / Dispatcher) + `chk_users_role` DB CHECK constraint preserved from Sprint-8 KTD7; no schema change to the enum surface in Sprint-9.
- R2. New per-tenant `role_permissions` table seeded by tenant migration; primary key is `(role, permission_key)`; Owner can edit via admin surface but only `role_permissions` rows for non-Owner roles (cannot strip Owner's permissions to prevent self-lockout).
- R3. Permission catalog enumerates ~20-30 permission keys covering every non-anonymous endpoint across Inventory, Inbound, Outbound, Channel, StockSync, Analytics, and Auth admin; catalog is a static `PermissionKeys` constants class in SharedKernel; ce-plan maps each existing `[Authorize]` (and the one `[Authorize(Roles = "Owner")]` on `AuthAdminController`) to a specific permission key.
- R4. Default seed at tenant provisioning: Owner = ALL permissions; Picker = empty (Sprint-10+ adds picker-specific keys); Dispatcher = empty (Sprint-10+ adds dispatcher-specific keys).
- R5. JWT access token claims include a flattened `perm` array reflecting the user's role's permissions at issuance time; the array is regenerated on every refresh, so role-permission edits propagate within one access-token lifetime (≤15 min).
- R6. ASP.NET Core authorization policies map 1:1 to permission keys (`AddAuthorization` registers one policy per key); business-module endpoints replace plain `[Authorize]` with `[Authorize(Policy = "<key>")]`; `AuthAdminController`'s existing `[Authorize(Roles = "Owner")]` is rewritten to one or more permission-policy attributes; missing permission → 403 (distinct from 401 enumeration collapse).
- R7. Owner admin surface to view + edit `role_permissions` per role; changes emit `RolePermissionsChangedV1` to auth_audit_log + outbox.

**TOTP MFA**
- R8. New per-tenant `user_totp_secrets` table: `(user_id, secret_encrypted, algorithm, enrolled_at, last_used_at)`; secret encrypted at rest (AES-256, KEK from `Auth:TotpKek` config) so even a DB dump alone cannot mint OTP codes.
- R9. New per-tenant `user_recovery_codes` table: `(user_id, code_hash, used_at, created_at)`; 10 codes per user, hashed at rest via Argon2id, marked single-use on consumption.
- R10. `users.mfa_required` boolean column; default `true` for Owner (set at seed), `false` for Picker / Dispatcher; only Owner admin surface can flip.
- R11. `users.mfa_enrolled` boolean column; `false` until enrollment verify-OTP succeeds.
- R12. Enrollment flow: `POST /api/auth/mfa/enroll/begin` (returns QR provisioning URI + manual secret + transactional UUID, holds the candidate secret in short-lived server state) → user scans + enters first OTP → `POST /api/auth/mfa/enroll/verify` with UUID + OTP → persists secret + generates 10 recovery codes + sets `mfa_enrolled = true` + emits `MfaEnrolledV1`; recovery codes returned in this response ONCE.
- R13. Login challenge flow: when `mfa_enrolled = true`, password-verify returns 200 with `{ mfa_required: true, mfa_challenge_token }` instead of a token pair; user POSTs `/api/auth/mfa/verify` with challenge token + 6-digit OTP within 5 min → returns real token pair on success; OTP mismatch collapses to `auth.invalid_credentials` 401 AND increments `failed_login_count`.
- R14. Recovery-code fallback: same `/api/auth/mfa/verify` endpoint accepts an 8-char recovery code in lieu of OTP; code marked consumed on success; user sees remaining-codes count on next dashboard load (and a "low codes" toast warning when ≤3 remain).
- R15. Forced enrollment: if `mfa_required = true` AND `mfa_enrolled = false`, password-verify returns `{ mfa_enrollment_required: true, enrollment_token }`; frontend forces redirect to /mfa/enroll; no other route is accessible to the user until enrollment completes.
- R16. Self-service disable from profile menu: requires password re-verify; only permitted when `mfa_required = false`; emits `MfaDisabledV1`.
- R17. Admin surface: view MFA status per user; reset MFA (delete secret + codes + set `mfa_enrolled = false` → forces re-enrollment); CANNOT disable `mfa_required` on Owner role users (Sprint-9 invariant; relaxable in Sprint-10+ if needed).

**Account lockout + rate limiting**
- R18. `users.failed_login_count` + `users.locked_until` columns added by Auth module migration; both reset to 0 / NULL on successful login OR successful password reset.
- R19. Per-account lockout: 5 failed password attempts (or MFA OTP failures) within a 15-min sliding window → `locked_until = now + 15min`; subsequent login attempts during the lockout window collapse to `auth.invalid_credentials` 401 (silent per R6 enumeration prevention from Sprint-8); MFA failures count toward the same counter as password failures.
- R20. Per-IP token-bucket rate limit on `/api/auth/login`, `/api/auth/refresh`, `/api/auth/mfa/verify`, and `/api/auth/forgot-password`: 10 req/min per source IP; bucket exceeded returns 429 (the only non-collapsed status — generic rate-limit is not enumeration-sensitive); ASP.NET Core built-in `RateLimiter` middleware.
- R21. Owner manual unlock via admin surface: sets `locked_until = NULL` + `failed_login_count = 0`; emits `AccountUnlockedByOwnerV1` to auth_audit_log.
- R22. Lockout emits `AccountLockedV1` to outbox at the moment of lockout (NOT on every subsequent attempt during the window); Notification module consumes → Owner alert email.

**Chain-aware refresh-token reuse-detection**
- R23. Each login issues a fresh `chain_id` (Guid); `RefreshTokenRecord` Redis payload schema gains `chain_id` field; rotation propagates the chain_id to the successor token.
- R24. Tombstone TTL extends from Sprint-8's 60sec to 7d (matches the full refresh-token TTL); tombstone payload now carries `chain_id` + successor plaintext (for grace-window-replay return).
- R25. Within the 60-sec grace window, the existing Sprint-8 behavior is preserved unchanged: replay returns the SAME successor token (legitimate retry / multi-tab race) without triggering reuse-detection.
- R26. Post-grace replay detection: tombstone present + `now - rotated_at > 60sec` → DEL all refresh-token records matching the chain_id (kicks only that one browser session, NOT all of the user's sessions); current refresh returns `auth.refresh_reused` 401; emits `RefreshReuseDetectedV1` to outbox.
- R27. `RefreshReuseDetectedV1` payload: `chain_id`, `user_id`, `presented_token_hash`, `presenting_ip`, `user_agent`, `occurred_at` (UTC), `correlation_id`.
- R28. Notification module consumes `RefreshReuseDetectedV1` and emails the Owner role users for that tenant with workspace + affected user + presenting IP + UTC timestamp + remediation guidance.

**Password-reset email**
- R29. New per-tenant `password_reset_tokens` table: `(token_hash, user_id, created_at, used_at, expires_at)`; 30-min TTL; `token_hash` is SHA-256 of the plaintext (matches refresh-token storage discipline — never store plaintext).
- R30. `POST /api/auth/forgot-password` with `{ email, tenant_slug }` (or subdomain Host): always returns 200 + generic confirmation message (R6 enumeration silent); if email matches an active user AND per-account cooldown not active, generates 32-byte URL-safe token, persists hash, emits `PasswordResetRequestedV1` to outbox with plaintext token.
- R31. Notification module consumes `PasswordResetRequestedV1`, renders email with deep link `<workspace-host>/reset-password?token=<plaintext>`, sinks to mailer; idempotent on event-id.
- R32. `POST /api/auth/reset-password/confirm` with `{ token, new_password }`: validates token-hash + not-used + not-expired; on success → resets password via Argon2id + marks token used + revokes ALL of user's refresh tokens (security event) + emits `PasswordResetCompletedV1` to auth_audit_log + returns 204; subsequent reuse of the same token returns `auth.invalid_token` 401.
- R33. Per-account cooldown: max 1 forgot-password request per email per 5 min; second request within window returns 200 (R6 silent) but does NOT emit a new event; reduces email-spam vector + protects against reset-link harvesting via timing.
- R34. Frontend ships forgot-password screen (workspace + email + submit + always-on confirmation) and reset-password screen (new password + confirm + submit, token from URL); both deep-linkable.

**ShopFlow.Notification module (NEW)**
- R35. Notification module = 7th business module in the modular monolith; quartet shape (`ShopFlow.Notification.Domain` / `Application` / `Infrastructure` / `Api`); follows Sprint-2-redux Inbound + Sprint-5 StockSync precedent.
- R36. Consumes via MassTransit RabbitMQ; subscribes to `PasswordResetRequestedV1` + `RefreshReuseDetectedV1` + `AccountLockedV1` + `MfaEnrolledV1`; Auth module routes each via `AddOutboxRoute<T>(SendKind.Publish)` (K13 close pattern from Sprint-4).
- R37. `IMailerProvider` port; dev-mode `LoggingMailer` impl + Aspire AppHost mock SMTP container (Mailcatcher-style, exposed web-UI inbox for demos); prod-mode `SmtpMailerProvider` adapter slot — actual SendGrid / SES / Postmark provider key is an operational pre-flight, NOT shipped with Sprint-9 code.
- R38. Email templates: 4 ship with Sprint-9 (password-reset / chain-reuse-alert / account-locked-alert / mfa-enrolled-confirmation); plain-text + HTML variants per template; placeholders via ICU-style or string-interpolation (ce-plan picks).
- R39. Per-tenant idempotency on event-id (UNIQUE constraint in Notification's own per-tenant dedup table); replay-safe under MT redelivery.
- R40. Notification module ships with its own UnitTests + IntegrationTests projects; integration tests use the in-memory MT TestHarness + a fake `IMailerProvider` recorder (matches Sprint-5 StockSync U9 pattern).

**Auth audit log**
- R41. New per-tenant `auth_audit_log` table: `(id, event_type, user_id NULL, source_ip, user_agent, metadata jsonb, occurred_at UTC, correlation_id)`; partitioning is a Sprint-10+ concern (ship one table, not time-partitioned).
- R42. Events logged: `LoginSucceeded`, `LoginFailed` (with internal cause), `AccountLocked`, `AccountUnlockedByOwner`, `MfaEnrolled`, `MfaUsed`, `MfaDisabled`, `MfaResetByOwner`, `PasswordChanged`, `PasswordResetRequested`, `PasswordResetCompleted`, `RefreshIssued`, `RefreshRotated`, `RefreshReuseDetected`, `RolePermissionsChanged`.
- R43. OTel response-body redaction (Sprint-8 KTD9) extends to never log: `secret`, `recovery_code`, `new_password`, `current_password`, `mfa_otp`, `mfa_secret`, `enrollment_token`, `mfa_challenge_token`, `password_reset_token`.

**Frontend**
- R44. New routes: `/forgot-password`, `/reset-password`, `/mfa/enroll`, `/mfa/challenge`, `/profile/security`.
- R45. MFA enrollment screen: server-rendered QR provisioning URI (SVG or PNG) + manual entry secret + OTP verify input + recovery codes display (one-time render with copy-all + download-as-text + an "I have saved these codes" acknowledge gate before navigation away is enabled).
- R46. MFA login challenge screen: 6-digit OTP input + "use a recovery code instead" link → recovery code input.
- R47. Profile/security screen: shows MFA status + buttons (Enroll if `mfa_enrolled = false`, Disable if `mfa_required = false`, View recovery codes count if enrolled, Generate new recovery codes if low / lost).
- R48. Owner admin surface: MFA-status column on Users list; locked-accounts panel with manual unlock button; role→permissions editor (per role, checkbox grid against the static permission catalog).
- R49. axe a11y smoke harness extended to cover all new screens (Sprint-6 KTD11 / Sprint-7 KTD11 pattern: focus-trap + role + label).

**Validation gates**
- R50. Sprint-9 sign-off requires: `dotnet build ShopFlow.sln` → 0 errors + 0 warnings (Sprint-8.5 baseline preserved); all unit tests pass; all integration tests scaffolded (Skip-marked where appropriate per Sprint-1+ posture); frontend Vitest → no new failures vs Sprint-8.5 baseline of 394 passing / 3 pre-existing Sprint-7 a11y; axe a11y smoke clean on all new screens.
- R51. Key Technical Decisions (KTDs) captured ahead of execution in the U0 opening commit, matching Sprint-8's 10-KTD pattern.
- R52. ce-doc-review pass before U0 (Sprint-8 precedent: 18 fixes applied before execution started).

---

## Acceptance Examples

- AE1. **Covers R19.** Given a Picker user with `failed_login_count = 4` from attempts within the last 15 minutes, when they submit a 5th wrong password, then `locked_until = now + 15min`, an `AccountLockedV1` event is emitted to outbox, and the response is `auth.invalid_credentials` 401 (not "account locked"). A 6th attempt during the lockout window also returns `auth.invalid_credentials` 401 with no additional event emission.
- AE2. **Covers R17.** Given an Owner user with `mfa_required = true`, when another Owner attempts to flip `mfa_required` to `false` for that user via admin surface, then the request returns 422 with code `mfa_required_invariant_owner` and `mfa_required` remains `true`.
- AE3. **Covers R14.** Given a user with 10 recovery codes and `mfa_enrolled = true`, when they consume recovery code #1 during MFA verify, then `user_recovery_codes.used_at` is set for that row, the response includes `recovery_codes_remaining: 9`, and a second attempt to use the same code returns `auth.invalid_credentials` 401.
- AE4. **Covers R26, R27, R28.** Given a refresh token A that has been rotated to B more than 60 seconds ago, when A is presented to `/api/auth/refresh`, then every Redis key matching `refresh:{tenant}:{user_id}:*` for that chain_id is deleted (other chains for the same user are untouched), the response is `auth.refresh_reused` 401, and a `RefreshReuseDetectedV1` event is emitted that the Notification module consumes to send Owner an alert email.
- AE5. **Covers R20.** Given an attacker IP that has made 10 requests to `/api/auth/login` in the last minute, when an 11th request arrives, then the response is HTTP 429 (not collapsed 401) with `Retry-After` header. A legitimate user from a different IP at the same instant is unaffected.
- AE6. **Covers R6.** Given a Picker user whose `perm` claim does not include `inventory.adjust`, when they POST `/api/inventory/adjust`, then the response is HTTP 403 (distinctly NOT 401 — authorization vs authentication). The auth_audit_log records `AuthorizationDenied` with the requested policy key.
- AE7. **Covers R30, R33.** Given user A submits forgot-password for email B at time T, when user A submits forgot-password for email B again at T+2min, then both responses are 200 with the same generic confirmation, but only the first request emits `PasswordResetRequestedV1` to outbox.

---

## Success Criteria

- A portfolio reviewer can demo the full RBAC + MFA loop: Owner logs in (forced enrollment), Picker logs in (no MFA), Owner locks themselves out (5 wrong passwords) and recovers via password-reset email (visible in Mailcatcher), Owner sees a chain-reuse alert in inbox after deliberately replaying an old refresh token from a curl session.
- A future Sprint-10+ implementer can add a Picker-only `/api/outbound/picks` endpoint by writing exactly two things: a new permission key in the catalog + `[Authorize(Policy = "outbound.pick")]` on the controller. No new auth code; no Auth module changes.
- An attacker spraying common passwords against a known Owner email is rate-limited at IP layer (429 after 10/min) AND account-locked after 5 wrong (silent collapse to 401); the Owner sees the lockout email within minutes.
- A replay of a stolen refresh token, even from the same IP as the legitimate session, kicks the attacker's chain and emails the Owner without disrupting the legitimate session on its own chain.
- Sprint-9 sign-off doc captures KTDs, deviations, and the Sprint-10+ trade-off list with the same shape as Sprint-7 / Sprint-8 sign-offs (auditable handoff to the next sprint).

---

## Scope Boundaries

- OAuth / social login (Google, GitHub, etc.) — Sprint-8 trade-off #2; Sprint-10+.
- httpOnly cookie session migration (move access tokens out of localStorage) — Sprint-8 trade-off #6; requires kernel-level cookie issuance + SignalR auth coordination across all modules; Sprint-10+.
- Subdomain-routed CORS hardening (per-origin allowlist + preflight cache beyond Sprint-8's `TrustedHostSuffixes`) — Sprint-8 trade-off #7; Sprint-10+.
- Per-resource fine-grained permission scoping (e.g., Picker scoped to specific warehouse zones, Dispatcher scoped to specific channels) — the option-4 RBAC shape from the brainstorm; Sprint-10+ if/when warranted.
- New Picker-only or Dispatcher-only WMS feature surfaces (pick-list view, ops dashboard) — Sprint-9 ships the per-permission gating infrastructure ONLY; the role-specific UI lives in Sprint-10+.
- WebAuthn / hardware security keys / passkeys / biometric MFA — Sprint-9 ships TOTP only.
- SMS-based 2FA — insecure per OWASP modern guidance; never landing in this project.
- Email provider sign-up + DNS records (SPF/DKIM/DMARC) + real prod SMTP send — Notification module ships with `IMailerProvider` port + dev Mailcatcher + SendGrid/SES adapter slot; actual provider key + DNS is an operational pre-flight, not Sprint-9 code work.
- Big-data seed loader, `CREATE INDEX CONCURRENTLY`, million-row latency benchmark — Sprint-7.5 carry-overs still open.
- Pre-existing Sprint-7 a11y failures (OrderLineItems empty-table-header + useOrderMutations shared-Response) — out of scope until a dedicated a11y sweep sprint.
- `auth_audit_log` table partitioning / archival — ships unpartitioned in Sprint-9; partitioning is a Sprint-10+ ops concern.

---

## Key Decisions

- **Role→permissions mapping (not user→permissions)**: keep Sprint-8's fixed `UserRole` enum + `chk_users_role` CHECK; permissions attach to role, not user. Reduces the admin surface to one editor (role→permissions grid) instead of two (role assignment + per-user override surface), and keeps the JWT projection straightforward. Per-user overrides are Sprint-10+ if a real use case emerges.
- **Owner-required, others optional TOTP**: Owner is the high-value tenant target; forcing MFA on Owner balances security against onboarding friction for warehouse-floor Picker/Dispatcher staff. Mirrors how most B2B SaaS ships MFA.
- **Per-account lockout + per-IP rate limit combo**: both layers because each closes a different attack — single-account brute-force closes under account lockout, distributed credential-stuffing closes under per-IP rate limit. OWASP cautions that account-only lockout is itself a DoS vector; the per-IP layer mitigates that by failing the attacker's spray at the source before lockout fires.
- **Outbox-routed via new Notification module (not direct SMTP from Auth)**: the architectural-consistency call. Sprint-3-redux's fulfillment saga + Sprint-4's webhook receiver + Sprint-5's stock sync all rely on the outbox + MassTransit shape; introducing a synchronous external SMTP call inside Auth's request path would be the one inconsistent component. The new Notification module is also the natural home for Sprint-10+ event-driven user notifications (order placed, low-stock alert, etc.), so the foundation has compounding value.
- **Chain-ID + revoke-chain + Owner notify (not revoke-all-sessions)**: per-chain revocation gives precise blast-radius control — legitimate sessions on other browsers/devices aren't impacted by an attacker's stolen-token replay. The Owner notification gives operator visibility within minutes, which is the meaningful production-grade upgrade over Sprint-8's silent single-session logout.
- **Permission claims in JWT (not per-request DB lookup)**: trades a small access-token-lifetime staleness window for zero per-request DB hits. Role-permission edits propagate within ≤15 min (one access-token TTL); operators can force-refresh by revoking the user's refresh chain via admin surface if immediate effect matters.
- **Forced MFA enrollment via single-purpose enrollment token (not session-prolonging "half-login")**: prevents the failure mode where a user with `mfa_required = true` could navigate to other routes by ignoring the enrollment prompt. The enrollment token has one purpose and no general API access.
- **Sprint-9 sized for ~14 implementation units**: Sprint-8's 12 + ~2 for the Notification module bootstrap. Matches the Sprint-8 cadence; bigger than Sprint-7's 14, smaller than the original Sprint-5 ambition.

---

## Dependencies / Assumptions

- Sprint-8 Auth module is the foundation; Sprint-9 extends it. Specifically depends on: `AuthDbContext`, `User` aggregate, `IPasswordHasher` (Argon2id), `IRefreshTokenStore` (Redis), `JwtTokenIssuer`, `AuthOptions`, kernel `AddShopFlowDefaults` JwtBearer composition.
- MassTransit + RabbitMQ transport + outbox dispatcher: all in place from Sprint-2-redux W4 transport flip; Notification module slots into the existing pattern.
- Aspire AppHost (Sprint-0-redux U7) hosts the new Mailcatcher-style container in dev; Aspire's `WithReference(notification-api)` wiring lands in U-Notification-Api.
- `IRequestContext` per-tenant binding (Sprint-1-redux K12 pattern); Notification module follows the same scope-binding discipline since it consumes per-tenant events.
- `shopflow-migrate` per-module migration registry (Sprint-8 U10); Notification module registers via `IModuleMigrationRegistry`; default `role_permissions` seed runs alongside owner-seed.
- An OTP library is needed (e.g., `Otp.NET`); ce-plan picks. Library must support TOTP RFC 6238 + Base32 secret encoding + drift window configurable.
- Mailcatcher-style container choice (Mailcatcher / MailHog / Mailpit / Maildev) is ce-plan's call; the requirement is "captures SMTP + exposes a web inbox for demos + runs in Docker, Aspire-managed."
- `IMailerProvider` adapter shape can be SendGrid, SES, Postmark, or a generic SMTP client; ce-plan picks an interface that doesn't lock in one provider.

---

## Outstanding Questions

### Resolve Before Planning

(None — all product-shape decisions are settled in Key Decisions above.)

### Deferred to Planning

- [Affects R8][Technical] AES-256 KEK for TOTP secret encryption: where does the key live? Options: `Auth:TotpKek` config (matches `Auth:DevSecret` pattern; rotation requires re-encrypt sweep); Azure Key Vault / AWS KMS / HashiCorp Vault (production-grade but adds infra dep); env-var-only. ce-plan picks the simplest one consistent with Sprint-8's dev-secret-style.
- [Affects R12][Technical] TOTP enrollment-secret transactional storage: short-lived Redis key with TTL? In-memory cache? Encrypted stash in the enrollment JWT itself? ce-plan picks.
- [Affects R37][Needs research] Mailcatcher vs MailHog vs Mailpit vs Maildev: which has the best Aspire integration story + most stable upstream maintenance? Mailpit is the modern descendant of MailHog with active maintenance; default to that unless ce-plan finds a blocker.
- [Affects R38][Needs research] Email template engine: RazorEngine.NET? Fluid? Scriban? Or plain string interpolation with placeholders? ce-plan picks; simplest viable wins.
- [Affects R45][Technical] QR code rendering: server-side via QRCoder library returning SVG/PNG, or client-side via a JS library after the secret is delivered? Server-side avoids shipping the secret to a JS bundle; client-side is one fewer round-trip. ce-plan picks (lean: server-side).
- [Affects R20][Technical] Per-IP rate limit storage: ASP.NET Core's built-in `RateLimiter` middleware uses in-memory token buckets by default. Multi-instance deployments need a distributed bucket (Redis); Sprint-9 modular monolith ships one instance per module today, so in-memory is fine; mark as Sprint-10+ if the deployment shape changes.
- [Affects R31][Technical] Email deep-link host resolution: the reset-password link needs `<workspace-host>` (`<slug>.shopflow.com`) baked in; Notification module needs to resolve workspace → host. Options: pass `tenant_slug` in the event payload + Notification has a `WorkspaceUrlTemplate` config (`https://{slug}.shopflow.com`); or include the full URL in the event payload from Auth. ce-plan picks.
- [Affects R7][Technical] role→permissions admin surface: real-time validation that an Owner cannot strip critical Owner permissions (auth.admin.users / auth.admin.role-permissions); ce-plan picks the validation strategy (server-side guard list or client-side disable + server-side hard-check).
