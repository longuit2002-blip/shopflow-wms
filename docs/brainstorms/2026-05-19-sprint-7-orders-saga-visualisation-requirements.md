---
date: 2026-05-19
topic: sprint-7-orders-saga-visualisation
---

# Sprint-7 — Orders Saga Visualisation (Second Vertical Slice + SignalR)

## Summary

Sprint-7 ships ShopFlow WMS's **second frontend vertical slice** — an Orders saga-visualisation surface that watches a customer order move through the 11-state fulfillment saga (Placed → Reserved → AwaitingPick → … → Shipped). Orders arrive via both the existing Shopee webhook pipeline (Sprint-4/4.5) and a developer seed endpoint for fast iteration. SignalR push replaces 2-second polling across both Inventory and Orders, closing Sprint-6 trade-off #9.

---

## Problem Frame

Sprint-6 shipped the first frontend surface (Inventory × Owner). Two important things remain unproven:

1. **The methodology generalises.** Sprint-6 was the first vertical slice; one slice can be lucky. A second screen, against a different module's backend, is needed to prove the brainstorm-plan-unit-commit-signoff cadence + per-screen R/A/F/AE pattern transfers cleanly.
2. **The fulfillment saga has no UI.** Sprint-3-redux shipped an 11-state MassTransit state machine + 9 cross-module contracts + EF saga repository + per-tenant DbContext binding. Reviewers and future-self can only see this work by reading test code or watching `saga_state` SQL. For a portfolio whose narrative depends on the methodology + correctness story being *visible*, that's a gap.

Sprint-6 trade-off #9 (2-s polling, no SignalR) is fine for Inventory — stock levels move slowly. But the saga transitions Reserved → AwaitingPick within ~50 ms when a consumer hits a happy path. A 2-s polling window conflates those visually. Either polling gets faster per-screen (wasteful), or push lands as infrastructure. The saga UX is exactly the use case SignalR was built for, so trade-off #9 closes here rather than getting deferred indefinitely.

Without Sprint-7: the fulfillment saga remains invisible at the UI layer despite being the most architecturally interesting Sprint-3-redux deliverable; the portfolio has one screen rather than a navigable application; the methodology pattern has only one frontend data point.

---

## Actors

- A1. **Owner** (SME seller / `tenant_seller` role) — primary actor, inherits Sprint-6's baked JWT. Lands on Orders nav, sees order list, drills into detail, watches saga transition live, drills into reservation ledger via the Sprint-6 LedgerDrawer.
- A2. **Shopee webhook (existing Sprint-4/4.5 path)** — system actor. The mock fires a webhook → Channel receiver → `OrderImportedV1` → Outbound consumer → Order row + `OrderPlacedV1` → saga start.
- A3. **Developer seed endpoint** — system actor. Dev-mode "spawn a test order" surface bypassing the webhook path for fast iteration on the visualisation.
- A4. **MassTransit fulfillment saga (Sprint-3-redux)** — system actor. Transitions through 11 states; each transition writes a new audit row this sprint adds.
- A5. **SignalR Hub** — new shared infrastructure. Tenant-scoped groups; pushes state-change events to subscribed Owner sessions.
- A6. **Sprint-7.5+ developer** — inheritor. Closes the remaining Sprint-6 / Sprint-7 trade-offs (real auth, schema expansion, camelCase normalisation, compensation actions).

---

## Key Flows

- F1. **Owner navigates to `/orders` → list populated**
  - **Trigger:** Owner clicks Orders nav item.
  - **Actors:** A1 → Outbound list query → A1.
  - **Steps:** (1) Sprint-6's ComingSoon placeholder is replaced; (2) KPI strip + filter strip + table render; (3) initial fetch happens; (4) SignalR subscription opens; (5) on subsequent transitions, table refreshes without re-fetch.
  - **Outcome:** Live order list with current saga state pill per row.
  - **Covered by:** R1, R2, R10, R12.

- F2. **Shopee webhook fires → order appears in list**
  - **Trigger:** Mock Shopee instance POSTs a signed webhook.
  - **Actors:** A2 → Sprint-4/4.5 receiver → `OrderImportedV1` → Outbound consumer → `OrderPlacedV1` → A4 saga start → A5 SignalR push → A1.
  - **Steps:** (1) webhook accepted; (2) Order row created; (3) saga starts; (4) `saga_transitioned` SignalR event fires; (5) Owner's table appends the new row without refresh.
  - **Outcome:** End-to-end realism — webhook through to UI in ~1 s.
  - **Covered by:** R8, R11, R14.

- F3. **Developer fires seed endpoint → order appears in list**
  - **Trigger:** Dev clicks "Seed test order" (dev-mode UI) or POSTs the seed endpoint directly.
  - **Actors:** A3 → Outbound seed → A4 saga start → A5 SignalR push → A1.
  - **Steps:** (1) seed endpoint creates Order with N test line items from in-memory metadata; (2) `OrderPlacedV1` emits; (3) saga starts; (4) UI updates as F2.
  - **Outcome:** Fast iteration loop — order on screen in ≤ 500 ms.
  - **Covered by:** R9.

- F4. **Owner clicks order row → detail route**
  - **Trigger:** Click on table row.
  - **Actors:** A1 → Outbound detail + transitions queries → A1.
  - **Steps:** (1) navigate to `/orders/$orderId`; (2) detail query loads line items; (3) transitions query loads history; (4) pipeline + log render; (5) SignalR subscription scopes to this order.
  - **Outcome:** Pipeline + line items + transitions log all visible; saga state machine is observable.
  - **Covered by:** R3, R4, R5, R15.

- F5. **Saga transitions → pipeline + log update live**
  - **Trigger:** Any saga state transition (consumer-driven or timeout-driven).
  - **Actors:** A4 → audit-write Then-handler → A5 → A1.
  - **Steps:** (1) saga calls Then-handler; (2) row written to transitions audit table; (3) `saga_transitioned` SignalR event fires; (4) pipeline highlighted-node moves; (5) elapsed-time badge filled in for the just-completed segment; (6) transitions log appends a row.
  - **Outcome:** UI reflects backend state within ~100 ms when hub connected.
  - **Covered by:** R4, R5, R11, R14.

- F6. **SignalR connection drops → polling fallback**
  - **Trigger:** WebSocket connection severs.
  - **Actors:** A5 → A1 client.
  - **Steps:** (1) client detects disconnect; (2) reconnection backoff begins; (3) meanwhile, 2-s polling resumes; (4) on reconnect, polling pauses.
  - **Outcome:** UX degrades gracefully; freshness drops to Sprint-6 baseline rather than breaking.
  - **Covered by:** R12, R13.

- F7. **Saga fails → failure visualisation**
  - **Trigger:** Saga lands in `Cancelled` / `Failed` state (e.g., reservation can't satisfy).
  - **Actors:** A4 → A5 → A1.
  - **Steps:** (1) saga compensates per Sprint-3-redux; (2) terminal state writes transitions row; (3) pipeline highlights the failure node in error tokens; (4) transitions log shows the causing event.
  - **Outcome:** Failure is observable; no UI actions to recover (deferred).
  - **Covered by:** R7.

- F8. **Owner clicks line item → ledger drilldown**
  - **Trigger:** Click on line item row in detail page.
  - **Actors:** A1 → Sprint-6 LedgerDrawer.
  - **Steps:** (1) drawer opens; (2) reservation ledger for that line's SKU + reservation id renders.
  - **Outcome:** Sprint-6 primitive reused; forensic-spine connection between Orders and Inventory is visible.
  - **Covered by:** R6.

---

## Requirements

**Orders read surface**
- R1. Orders list route at `/orders` replaces Sprint-6's `ComingSoon` placeholder. KPI strip (active orders, awaiting pick, awaiting ship, failed today). Filter strip (status, channel, date range, search by external order id).
- R2. Orders table — columns: external order id, channel display, line count, current saga state pill, age, last-transition timestamp.
- R3. Orders detail route at `/orders/$orderId` — full route (not drawer). Layout: pipeline at top, line items table mid, transitions log bottom.
- R4. SagaPipeline component — horizontal pipeline with one node per saga state in canonical order. Current node lit; elapsed-time badge per completed segment; failure nodes render in error tokens.
- R5. TransitionsLog component — append-only feed with timestamp + from-state + to-state + elapsed-since-previous, newest at top.
- R6. Line items table on detail page reuses Sprint-6's `<LedgerDrawer>` primitive — clicking a line item opens drawer with that line's reservation ledger entries.
- R7. Failure state visualisation — when saga lands in `Cancelled` or `Failed`, pipeline highlights the failure node and transitions log shows the causing event. No UI actions to recover this sprint.

**Order arrival**
- R8. Real Shopee webhook path (existing Sprint-4/4.5 receiver → Channel.OrderImportedV1 → Outbound.OrderImportedConsumer → OrderPlacedV1 → saga start) is exercised end-to-end and surfaces in the Orders list within ~1 s.
- R9. Developer seed endpoint — dev-mode only. Creates a synthetic Order with N test line items (sourced from existing in-memory SKU metadata) and starts the saga. UI "Seed test order" affordance visible only when a dev-mode flag is active.

**SignalR push infrastructure**
- R10. SignalR Hub registered via `AddShopFlowDefaults` so all module APIs expose the same hub URL. Tenant-scoped groups; JWT auth via SignalR's access-token query-parameter pattern. Hub is shared infrastructure, not per-module.
- R11. SignalR event contracts — `stock_changed` (Inventory; replaces Sprint-6 polling-driven invalidation) and `saga_transitioned` (Outbound; new). Envelopes carry `tenant_id`, `correlation_id`, and `occurred_at` per AGENTS.md rule 42.
- R12. Frontend SignalR client — connection management + reconnection with exponential backoff + tenant-scoped subscription. On hub events, invalidates TanStack Query keys (no payload-driven cache updates this sprint — server is source of truth).
- R13. Inventory polling stays as fallback — Sprint-6's polling code is not deleted. Client either receives SignalR events (preferred) or falls back to existing 2-s polling when hub is disconnected. Hook signatures unchanged per Sprint-6 KTD5.

**Saga history persistence**
- R14. New per-tenant transitions audit table in Outbound. Saga writes a row on every state transition via a Then-handler. Columns: correlation_id, from_state, to_state, occurred_at, event_type that triggered. Migration ships as the only schema delta this sprint.
- R15. Backend query — list transitions for one order, ordered by occurred_at.

**Cross-cutting**
- R16. Wire shape stays PascalCase (Sprint-6 KTD4 unchanged). camelCase normalisation remains deferred.
- R17. A11y axe-smoke harness extended to cover new Orders surfaces (list, detail, SagaPipeline, TransitionsLog) — same axe-clean bar as Sprint-6.
- R18. New endpoints inherit Sprint-6's auth + tenant routing — JWT bearer in `Authorization`, tenant_slug echoed in `X-Tenant-Slug`, `Idempotency-Key` on writes (seed endpoint counts).

**CI + repo wiring**
- R19. CI frontend job (Sprint-6 baseline) covers new tests by default. No CI changes needed unless surface or test framework choice warrants.
- R20. Sign-off doc + tag `v0.10.0-sprint-7-orders` ship at sprint close; CHANGELOG + README + CLAUDE.md current-stage updated.

---

## Acceptance Examples

- AE1. **Covers R1, R2, R8, R11, R12.** Given Owner is on `/orders`, when a Shopee webhook fires for a new order, then within ~1 s the new order appears in the table without manual refresh, and the row shows current saga state pill = "Reserved" (or further) and age = "just now".

- AE2. **Covers R3, R4, R5, R14, R15.** Given Owner clicks an order row, when `/orders/$orderId` mounts, then horizontal pipeline renders with all 8 canonical saga states, the current state is highlighted, elapsed-time badges fill the completed segments, and the transitions log below shows one row per past transition (newest at top).

- AE3. **Covers R4, R11.** Given Owner is on order detail and saga is at AwaitingPick, when consumer transitions saga to Picked, then pipeline's lit node advances from AwaitingPick to Picked within ~200 ms (SignalR-connected) without polling, and a new row appears at the top of the transitions log.

- AE4. **Covers R7.** Given an order is placed but stock is insufficient, when saga compensates and lands in Cancelled, then pipeline shows the Reserved node in error tokens and the transitions log shows the `StockReservationFailedV1` event as the causing transition. No "retry" button renders.

- AE5. **Covers R6.** Given Owner is on order detail, when they click a line item row, then Sprint-6's `<LedgerDrawer>` opens showing that SKU's reservation ledger entries, filtered to entries referencing this order's line ids.

- AE6. **Covers R12, R13.** Given Owner is on `/orders` with hub connected, when the hub connection drops, then the client logs the disconnect, falls back to 2-s polling (Sprint-6 behaviour), and on reconnect resumes SignalR-driven updates without page reload.

- AE7. **Covers R9.** Given dev mode is active, when developer fires the seed endpoint, then an Order row is created with N test line items, saga starts, and within ~500 ms the new order appears in the Owner's list.

- AE8. **Covers R17.** When automated a11y smoke test runs against `/orders`, `/orders/$orderId`, and SagaPipeline isolated, then axe-clean (no critical or serious violations) per Sprint-6's bar.

---

## Success Criteria

- Owner reviewer can open `/orders`, see populated table fed by both webhook + seed paths, click any row, and watch the saga pipeline animate through transitions live with sub-second latency when hub is connected.
- Sprint-6's polling is replaced by SignalR-driven invalidation (with polling intact as fallback); UI freshness latency on Orders detail drops from ~2 s average to under ~200 ms when hub connected.
- Fulfillment saga from Sprint-3-redux has a visible UI surface for the first time — pipeline + transitions log + failure visualisation all observable.
- Sprint-7.5 planner can read this sprint's sign-off and start the next trade-off-closure sprint (real auth, schema expansion, compensation actions) without re-asking product questions.
- ~14 implementation units shipped on `feat/sprint-7-orders-saga-visualisation` branch cut from `v0.9.0-frontend-vertical-slice`.

---

## Scope Boundaries

### Deferred for later (named follow-up sprints)

- **Compensation actions in UI** (`mark-pick-failed`, `cancel-order`, retry buttons) — Sprint-7.5 candidate.
- **Real auth module** (password hashing, refresh tokens, role claims, MFA placeholder) — replaces Sprint-6 fake JWT. Sprint-8 candidate.
- **Cosmetic SKU schema expansion** (Sprint-6 trade-off #1: name, category, threshold columns) — Sprint-7.5.
- **camelCase wire normalisation** (Sprint-6 trade-off #6) — Sprint-7.5.
- **Flash-sale dual-write** (Sprint-6 trade-off #10: also write StockSync `/flag` from the toggle path) — Sprint-7.5.
- **URL-search-params persistence** for filter state (Sprint-6 trade-off #4) — Sprint-7.5+.
- **Reservation ledger cursor pagination** (Sprint-6 trade-off #5) — Sprint-8+.
- **Multi-role auth** — separate picker / packer / ops-dispatcher surfaces. Sprint-8+ when real auth lands.
- **Operator role functionality** — entire role + mobile pick-wave UI + 768px breakpoint. Sprint-8+.
- **Inbound module UI** (PO list / GRN form / reconciliation tickets) — Sprint-9+.
- **Cross-module joins beyond what Order already stores** — architectural decision deferred; reviewable later if a screen genuinely needs it.
- **Sprint-5.5 scale-gate harness closure** — pre-existing follow-up; remains parallel-track.

### Out of scope (rejected — not in this product's identity)

- **End-customer-facing screens** (order tracking, returns portal) — ShopFlow is a WMS, not a storefront.
- **Mobile UI for Owner role** — design canon enforces 1024 px floor for non-Operator roles.
- **Alternative saga widget shapes** considered and rejected in dialogue: vertical state list, transitions-log-only, pipeline+log combo.
- **Alternative detail layouts** considered and rejected in dialogue: wider drawer, two-pane (list + persistent detail), hybrid drawer+route promotion.
- **Dark mode** — Phase-3 work; light-only intentionally.
- **PDF exports** (saga audit report, compliance reports) — Phase-3.
- **Sub-orders / partial shipments / RMA flow** — out of v1 scope.

---

## Key Decisions

- **Orders second-vertical-slice chosen over "real-auth focused" / "Sprint-6 polish bundle" / "Sprint-7 as written"**: highest portfolio leverage — proves the methodology pattern generalises and exercises the most architecturally interesting backend (Sprint-3-redux saga + reservation ledger). Trade-off closures slot into Sprint-7.5+ rather than bundling 5 closures into one sprint and risking under-baked real auth.
- **Read-mostly saga-visualisation framing chosen over compensation-as-headline / multi-action operator console**: bounds scope to Sprint-6 cadence (~14 units); avoids ballooning into multi-sprint operator console; failure states still visualised so the saga's correctness story is not hidden.
- **SignalR push everywhere chosen over Orders-only / faster polling / append-only log absorbing freshness**: saga UX is the use case SignalR was built for; closes Sprint-6 trade-off #9 fully in one go rather than partially. Inventory hook signatures already designed for this swap per Sprint-6 KTD5.
- **Horizontal pipeline chosen over vertical list / transitions-log-only / pipeline+log combo**: most portfolio-impactful visual; demands width which drives the full-route decision below.
- **Full route chosen over wider-drawer / two-pane / hybrid drawer+route**: URL-shareable (portfolio screenshots); pipeline + line items + log won't fit comfortably in a 400-800 px drawer; LedgerDrawer reuse still happens *from* the detail page for line items.
- **Both arrival paths (webhook + seed) chosen over either alone**: webhook proves the Sprint-4/4.5 closure visually; seed gives a fast demo iteration loop. Webhook-only would gate demo on the mock; seed-only would leave the Sprint-4/4.5 pipeline silently unexercised in the UI layer.
- **New per-tenant transitions audit table chosen over outbox-derived / sole-state-row approaches**: clean separation — single-row `saga_state` stays MT-owned (current state only); new transitions table is Outbound-owned (append-only history). Outbox-derived was tempting but would couple the visualisation to outbox plumbing internals.
- **Shared SignalR hub via `AddShopFlowDefaults` chosen over per-module hubs**: matches the outbox infrastructure shape (shared registration + per-module subscribers); avoids URL-fragmentation across 6 future microservices; tenant-scoped groups isolate fan-out naturally.
- **Polling code retained as SignalR-disconnect fallback rather than deleted**: graceful degradation; UX continues to function when hub drops; Sprint-6 code is still load-bearing rather than scaffolding.
- **Wire shape stays PascalCase for new endpoints**: consistency with Sprint-6 KTD4; camelCase normalisation lands as a single migration in Sprint-7.5 instead of churning during this sprint.

---

## Dependencies / Assumptions

- **Branch from `v0.9.0-frontend-vertical-slice`** — fresh `feat/sprint-7-orders-saga-visualisation` branch.
- **Outbound module from Sprint-3-redux is operational** — `Order` aggregate, `OrdersController` (POST/GET), `FulfillmentSaga` state machine with 11 states, EF saga repository with K12 per-tenant DbContext binding. Verified: `Order` stores `ChannelExternalOrderId` and `ShippingProfile`; `FulfillmentSagaState` exposes `CurrentState` and `UpdatedAt` only — **no history**, hence R14's new audit table.
- **Channel module from Sprint-4/4.5 is operational** — Shopee receiver + `OrderImportedV1` contract + Outbound consumer wired. Mock at `tools/mocks/shopee/` is registered in AppHost.
- **Sprint-5 StockSync outbox path is operational** — `StockLevelChangedV1` emits from Inventory's stock-mutating paths; SignalR's `stock_changed` event composes from the same source.
- **Sprint-6 frontend scaffolding intact** — TanStack Router (file-based), TanStack Query, Zustand, design tokens, primitives (`Drawer`, `Modal`, `Toast`, `Toggle`, `LedgerDrawer`, `AllocationBar`, `KpiStrip`).
- **ASP.NET Core SignalR is available** — no new top-level dependency beyond `Microsoft.AspNetCore.SignalR` (in-box for ASP.NET Core).
- **`@microsoft/signalr` JS client** — new pnpm dependency.
- **Frontend tooling unchanged** — Node 20+, pnpm 10.32.1 (pinned via `packageManager`).
- **TenantRoutingMiddleware compatibility with SignalR `/negotiate`** — assumed treatable via the `[SkipTenantRouting]` precedent from Sprint-4. Plan-time verification needed (see Outstanding Questions).
- **No new EF migrations besides R14's transitions audit table** — Sprint-6 trade-off #1 (cosmetic SKU columns) remains deferred.
- **Backend builds in CI only** on the current dev machine (.NET 8.0.407 vs the repo's `global.json`-pinned 9.0.305). Sprint-1+ established posture.

---

## Outstanding Questions

### Resolve Before Planning

*(none — all product decisions captured in this brainstorm + Sprint-6 sign-off + Sprint-3-redux reference)*

### Deferred to Planning

- [Affects R10][Technical] SignalR hub URL convention — single shared `/hub` vs per-module path (`/inventory/hub`, `/orders/hub`). Plan decides based on `AddShopFlowDefaults` composition shape.
- [Affects R12][Technical] Reconnection backoff parameters — exponential with jitter is conventional; plan confirms initial delay + max delay + retry cap. SignalR client's default policy may suffice.
- [Affects R9][Technical] "Seed test order" dev-mode flag mechanism — env var (`ShopFlow__SeedEnabled=true`), feature-flag header, or build-time `import.meta.env.DEV`. Plan decides.
- [Affects R14][Technical] Transitions audit-write mechanism — saga `Then`-handler vs DbContext `SaveChangesInterceptor` vs outbox-event-driven consumer. `Then`-handler is the simplest and keeps the write co-transactional with the saga state row; plan confirms.
- [Affects R2][Technical] Channel display in orders list — `Order.ChannelExternalOrderId` parsing (e.g. detect "SHOPEE_" prefix) vs adding small `Order.ChannelType` column. Plan decides; second option needs migration.
- [Affects R10][Technical][Needs research] `TenantRoutingMiddleware` + SignalR `/negotiate` — verify the existing `[SkipTenantRouting]` precedent (Sprint-4 webhooks) applies to SignalR's negotiation path, or whether tenant routing needs to happen earlier in the SignalR auth flow. Likely the JWT's `tenant_slug` claim flows through SignalR's auth identity and binds at hub-method invocation time.
- [Affects R7][Technical] Failure-event surfacing — saga lands in `Cancelled` via several distinct compensation paths (`StockReservationFailedV1`, pick-fail compensation, etc.). Plan decides which root-cause event label the transitions log displays.
- [Affects R20][Process] Tag naming — `v0.10.0-sprint-7-orders` or `v1.0.0-sprint-7-orders` (signal "ten sprints in, real app shape")? Cosmetic; plan decides.

### Roadmap context (high-level — separate brainstorm/plan when reached)

- **Sprint-7.5** — bundled trade-off closures: compensation actions in UI + cosmetic SKU schema expansion + camelCase wire normalisation + flash-sale dual-write + URL-search-params persistence. Estimated ~1 week point release. Pattern matches Sprint-2.5 / 4.5 / 5.5 cadence.
- **Sprint-8** — real auth module (replaces Sprint-6 fake) + first multi-role surface (picker or ops dispatcher). Estimated 2 weeks.
- **Sprint-9** — Channels screen + Compliance screen. Estimated 2 weeks.
- **Sprint-10** — Onboarding wizard + Settings + Tenants Admin. Estimated 2 weeks.
- **Sprint-5.5** — scale-gate harness closure (pre-existing follow-up). Parallel-track if capacity allows.
- **Phase-3 polish** — Inbound module UI design + implementation. Sprint-11+.
