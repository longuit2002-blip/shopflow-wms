# Sprint history

The chronological release record for ShopFlow WMS, newest first. Each tag has a phase-gate sign-off with the full detail (deviations, KTDs, verification numbers); this page is the index. For *what supersedes what*, see [CHANGELOG.md](CHANGELOG.md). For the engineering worth reading first, see the [README](../README.md#the-four-hard-problems-and-the-tests-that-prove-them).

> **Portfolio finish-line** (in progress, post-`v0.17.0-sprint-13`): makes the four hard problems demonstrable locally (`task proofs`) and the multi-channel claim honest (Lazada as the second adapter). Requirements + status: [docs/brainstorms/2026-05-27-portfolio-finish-line-requirements.md](brainstorms/2026-05-27-portfolio-finish-line-requirements.md).

| Tag | What landed | Sign-off |
|---|---|---|
| `v0.17.0-sprint-13` | Packer fourth role + 4-role hand-off (Picker→Packer→Dispatcher) + `MarkPackFailed` saga Path D | [signoff](phase-gates/2026-05-27-sprint-13-signoff.md) |
| `v0.16.1-sprint-12.5` | Trade-off closures: `auth_audit_log` wiring (12 handlers) + saga `actor_user_id` + `MarkShipFailed` Path C + tier-3 carrier-retry E2E | [signoff](phase-gates/2026-05-26-sprint-12.5-signoff.md) |
| `v0.16.0-sprint-12` | Dispatcher (2nd non-Owner role) + 3-role hand-off proof + cross-role denial tests | [signoff](phase-gates/2026-05-22-sprint-12-signoff.md) |
| `v0.15.0-sprint-11` | First multi-role surface: the Picker role end-to-end under a narrowed `perm[]` claim | [signoff](phase-gates/2026-05-22-sprint-11-signoff.md) |
| `v0.14.1-sprint-10.5` | Trade-off closures: frontend `usePerm` gating + 403 wire-shape integration tests + catalog drift fix | [signoff](phase-gates/2026-05-22-sprint-10.5-signoff.md) |
| `v0.14.0-sprint-10` | Backend per-action `[Authorize(Policy=...)]` migration across 33 actions | [signoff](phase-gates/2026-05-22-sprint-10-signoff.md) |
| `v0.13.0-sprint-9.5` | Notification module (7th quartet) + frontend auth UX (5 screens + admin surface) + cross-tenant integration tests | [signoff](phase-gates/2026-05-21-sprint-9.5-signoff.md) |
| `v0.12.0-sprint-9` | Backend auth hardening: per-permission RBAC + TOTP MFA + lockout + chain-aware refresh reuse detection | [signoff](phase-gates/2026-05-20-sprint-9-signoff.md) |
| `v0.11.1-sprint-8.5` | Test sweep + .NET 9 build-error cleanup + OwnerSeed integration test | [signoff](phase-gates/2026-05-20-sprint-8.5-signoff.md) |
| `v0.11.0-sprint-8` | Real auth module: Argon2id hashing + HS256 JWT + Redis refresh tokens + Owner admin surface | [signoff](phase-gates/2026-05-20-sprint-8-signoff.md) |
| `v0.10.1-sprint-7.5` | Production-ready closures: camelCase wire + rich `skus` table + reservation-ledger cursor pagination + index audit | [signoff](phase-gates/2026-05-20-sprint-7.5-signoff.md) |
| `v0.10.0-sprint-7-orders` | Orders saga visualisation: `<SagaPipeline>` + SignalR push + `outbound_saga_transitions` audit | [signoff](phase-gates/2026-05-19-sprint-7-signoff.md) |
| `v0.9.0-frontend-vertical-slice` | First frontend surface: Vite + React 19 + TanStack Router/Query; Inventory screen end-to-end | [signoff](phase-gates/2026-05-19-sprint-6-signoff.md) |
| `v0.8.0-methodology-writeup` | [docs/methodology.md](methodology.md) — the AI-assisted development case study | [signoff](phase-gates/2026-05-18-methodology-writeup-signoff.md) |
| `v0.7.0-sprint-5` | StockSync engine: coalescing buffer + per-channel token bucket + priority queue + circuit breaker | [signoff](phase-gates/2026-05-17-sprint-5-signoff.md) |
| `v0.6.1-sprint-4.5` | Webhook follow-up: marketplace-asserted `provider_event_id` + `OrderImportedV1` + scale-gate harness | [signoff](phase-gates/2026-05-15-sprint-4.5-signoff.md) |
| `v0.6.0-sprint-4` | Channel connections + webhook idempotency (`UNIQUE(channel_id, provider_event_id)`) + Shopee adapter | [signoff](phase-gates/2026-05-13-sprint-4-signoff.md) |
| `v0.5.0-sprint-3-redux` | Outbound module + fulfillment saga (11 states) + pick waves — Phase-1 customer funnel closed | [signoff](phase-gates/2026-05-13-sprint-3-redux-signoff.md) |
| `v0.4.1-sprint-2.5` | Per-module outbox table-name prefix + first cross-module flow integration tests | [signoff](phase-gates/2026-05-13-sprint-2.5-signoff.md) |
| `v0.4.0-sprint-2-redux` | Inbound module (PO + receiving) + bin/zone schema + MassTransit RabbitMQ transport flip | [signoff](phase-gates/2026-05-13-sprint-2-redux-signoff.md) |
| `v0.3.0-sprint-1-redux` | Reservation ledger: conditional-CTE INSERT at READ COMMITTED (the hot-key flash-sale solution) | [signoff](phase-gates/2026-05-12-sprint-1-redux-signoff.md) |
| `v0.2.0-phase-0-redux` | Foundation + tenancy: control-plane catalog, tenant-routing middleware, shared kernel, analyzers, Aspire AppHost | [signoff](phase-gates/2026-05-12-phase-0-redux-signoff.md) |

The pre-redesign v2.0 RLS-shared design is archived at branch `archive/phase-1-sprint-1-rls-shared` and tag `archive/v0.1.0-phase-0-rls-shared`.
