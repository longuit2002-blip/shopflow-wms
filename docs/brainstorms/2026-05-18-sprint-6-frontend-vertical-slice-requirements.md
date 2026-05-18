---
date: 2026-05-18
topic: sprint-6-frontend-vertical-slice
---

# Sprint-6 — Frontend Vertical Slice MVP (Inventory × Owner)

## Summary

Sprint-6 ships a frontend vertical slice MVP — Inventory screen × Owner role end-to-end — using React + TypeScript + Vite + token-based CSS trong `web/` subdirectory của shopflow-wms repo. Login screen real-UI nhưng calls fake `/auth/login` returning baked JWT (dev mode); 2-second polling thay SignalR; Reservation Ledger drawer là forensic-spine demo từ Sprint-1-redux. 8 màn khác là "Coming Sprint-X" placeholders. Sprint-7+ progressively swap fake auth + polling sang real auth module + SignalR hub. Cadence: continues Sprint-2.5 / 4.5 / 5.5 deferral pattern.

---

## Problem Frame

Backend đã ship 7 sprints + Phase-3 methodology writeup (tag `v0.8.0-methodology-writeup`); 6 logical modules ship complete (ControlPlane, Inventory, Inbound, Outbound, Channel, StockSync) plus methodology case study. Toàn bộ business logic — reservation ledger atomic correctness, fulfillment saga, channel ingress, stock-sync egress — đã proven end-to-end ở backend test layer. **Project chưa có frontend.**

Design handoff đã land tại `D:\side_projects\Shopflow\design_handoff_shopflow_wms\`:
- 16 HTML/JSX prototype files (high-fidelity, 5 screens × 3 roles, amber-ochre tokens, Vietnamese content, 10 design notes anchored via `data-review`)
- `INTEGRATION_INTENT.md` (~7350 từ) — backend contracts mapping per-screen + auth/role matrix + 14 SignalR events + 12 backend gaps + UX states + onboarding + settings
- `STYLING_SPECS.md` (~6800 từ) — assets inventory + typography pinned (IBM Plex Sans/Mono) + tokens.css delta patch ready-to-paste + motion specs + locale formatting + a11y floor + 20 grab-bag decisions

Without Sprint-6 + follow-up frontend sprints:
- 7 sprints of backend correctness work has no user-facing demonstration
- The "DB-per-tenant surfaced as product feature" wedge claim has no UI to surface it
- Portfolio narrative depends on README + code reading; no visual artifact for reviewers

This sprint starts the multi-sprint frontend phase. Vertical slice MVP (1 screen × 1 role end-to-end) is the proof-of-integration; subsequent sprints expand to the full 5-screen × 3-role surface.

---

## Actors

- A1. **Owner** (SME seller / `tenant_seller` role) — primary actor for this slice. Logs in, manages inventory: views SKU table with channel allocation chips, opens drawer to inspect reservation ledger (forensic spine), adjusts stock, sets threshold, toggles flash-sale flag, creates new SKUs.
- A2. **Frontend integration developer (future-self / dev cloning repo)** — secondary actor. Cần infrastructure trong `web/` để add screens in Sprint-7+ without re-deciding scaffold / tokens / routing / auth shape.
- A3. **Backend (existing 6 modules)** — provides endpoints. Inventory module surface (skus list, ledger, adjustments, thresholds, create) needs HTTP layer exposed; rest of needed surfaces (StockSync SkuFlag) exist from Sprint-5 U7.
- A4. **Sprint-7+ developer** — inheritor. Replaces fake auth with real auth module; replaces 2s polling with real SignalR hub; adds remaining 8 screens behind existing nav stubs.

---

## Key Flows

- F1. **Owner login → Inventory landing**
  - **Trigger:** Owner navigates to root URL.
  - **Actors:** A1 → fake `/auth/login` → A3 (TenantRoutingMiddleware reads JWT claim `tenant_slug`) → A1.
  - **Steps:** (1) Owner sees login screen với amber-ochre tokens; (2) types email + password; (3) submits → fake `/auth/login` returns baked JWT (any non-empty credentials accepted in dev mode); (4) frontend stores JWT in localStorage; (5) routes to `/inventory` (default landing for Owner role); (6) Inventory screen mounts and fetches SKU table + KPI strip.
  - **Outcome:** Owner sees populated Inventory screen với real backend data, polled every 2s.
  - **Covered by:** R1, R3, R7.

- F2. **Owner opens Reservation Ledger drawer for a SKU**
  - **Trigger:** Owner clicks a row in the SKU table.
  - **Actors:** A1 → A3 (`GET /api/v1/inventory/skus/<sku>/ledger`).
  - **Steps:** (1) drawer slides in (150ms ease-out from STYLING_SPECS §4); (2) fetch ledger entries for the SKU; (3) render append-only ledger với `running_balance` column + per-channel allocation bar; (4) drawer remains open until Esc / click-outside / X.
  - **Outcome:** Forensic ledger demo — every reservation / release / confirm / adjust visible with timestamp + ref_order + idempotency_key.
  - **Covered by:** R4, R8.

- F3. **Owner adjusts stock for a SKU**
  - **Trigger:** Owner clicks "Điều chỉnh tồn" in drawer or row action.
  - **Actors:** A1 → A3 (`POST /api/v1/inventory/adjustments` with `Idempotency-Key` header).
  - **Steps:** (1) modal opens với delta + reason + note fields; (2) Owner submits; (3) frontend includes `Idempotency-Key: <ulid>` header; (4) backend writes `stock_adjustments` row + adjusts `stock_items` + emits domain event (which downstream emits `StockLevelChangedV1` via Sprint-5 U2 path); (5) within 2s, polling re-fetches SKU table + drawer ledger shows new entry; (6) toast confirms "Đã điều chỉnh".
  - **Outcome:** Audit trail written; UI converges; ledger shows the adjustment as new row.
  - **Covered by:** R5, R8, R10.

- F4. **Owner toggles `is_flash_sale` flag on a SKU**
  - **Trigger:** Owner clicks flash-sale toggle in drawer.
  - **Actors:** A1 → A3 (`PUT /api/v1/skus/<sku>/flag` — Sprint-5 U7 endpoint).
  - **Steps:** (1) toggle flips optimistically; (2) request fires; (3) success → toggle remains; (4) failure → revert + error toast.
  - **Outcome:** `stock_sync_sku_flag` table updated; first UI surface for Sprint-5 StockSync's `SkuFlag` aggregate.
  - **Covered by:** R6, R10.

- F5. **Owner clicks any other nav item → sees Coming Sprint-X placeholder**
  - **Trigger:** Owner clicks Dashboard / Orders / Channels / Compliance / Audit / Onboarding / Settings / Tenants Admin.
  - **Actors:** A1 → frontend route → `<ComingSoon>` component.
  - **Steps:** (1) route matches; (2) `<ComingSoon screen="dashboard" targetSprint={7}>` renders với Lucide icon + screen name + "Coming Sprint-X" message + brief roadmap blurb.
  - **Outcome:** Owner sees credible "not built yet" state; nav remains navigable.
  - **Covered by:** R11.

---

## Requirements

**Frontend foundation**
- R1. Scaffold React + TypeScript + Vite project in `web/` subdirectory of shopflow-wms repo. Package manager: pnpm. Configure Vite for `/web` base path, port 5173 dev, hot reload, source maps.
- R2. Token-based CSS layer extracted from prototype's `tokens.css` + `tokens-settings.css`, with the STYLING_SPECS §3.3 delta patch applied (`--neutral-300`, `--text-3xl/4xl`, `--ok-ink`, focus ring tokens, motion tokens, z-index scale). All values from prototype reproduced 1:1; no Tailwind defaults substituted.
- R3. Login screen with amber-ochre tokens — logo, email input, password input, TOTP placeholder (stub), submit button. Real UI; calls fake `/auth/login` returning baked JWT for dev mode. Bilingual via `i18n.jsx` reuse (vi-VN default + en-US toggle).

**Inventory + Owner read surface**
- R4. SKU table with all columns from prototype's `<DesktopInventory>`: SKU, name, category, on-hand, reserved, available, threshold, channel allocation chips, status pill, zone, last_updated. Filter strip (category, channel, state, zone, search) round-trips to server (no in-memory filter per INTEGRATION §1).
- R5. KPI strip — total stock, reserved, below threshold, oversell risk — feeding from new `GET /api/v1/inventory/summary` aggregate endpoint (Backend Gap, U-level closure).
- R6. Reservation Ledger drawer (`<LedgerDrawer>` shape) — paginated append-only ledger entries with `running_balance`, per-channel allocation bar at top.
- R7. 2-second polling for SKU table refresh + drawer ledger refresh (when drawer open). No SignalR.

**Inventory + Owner write surface**
- R8. **Adjust stock** — modal with delta + reason + optional note. POST `/api/v1/inventory/adjustments` with required `Idempotency-Key: <ulid>` header. Toast confirmation.
- R9. **Set threshold** — inline edit per SKU or modal. PUT `/api/v1/inventory/skus/<sku>/threshold` with required `Idempotency-Key` header.
- R10. **Toggle `is_flash_sale`** — drawer toggle. PUT `/api/v1/skus/<sku>/flag` (Sprint-5 U7 endpoint, already exists). Optimistic UI with revert on failure.
- R11. **Create SKU** — modal with sku / name / category / threshold / initial_total / zone / price / cost / alloc fields. POST `/api/v1/inventory/skus`.

**Stub treatment for 8 other screens**
- R12. Shared `<ComingSoon screen={name} targetSprint={N}>` component rendering: Lucide icon for the screen + screen name + "Coming Sprint-X" message + 1-paragraph roadmap blurb. Renders for: Dashboard, Orders, Channels, Compliance, Audit, Onboarding, Settings, Tenants Admin. Sidebar nav remains fully visible; click → placeholder route.

**Cross-cutting platform decisions**
- R13. Auth + tenant routing — frontend includes JWT in `Authorization: Bearer <jwt>` header on every API call plus `X-Tenant-Slug: <slug>` echo per INTEGRATION §2. Backend TenantRoutingMiddleware reads JWT claim and validates.
- R14. Locale support — vi-VN default, en-US toggle via existing `i18n.jsx` patterns. Persist locale choice to `localStorage.shopflow_lang`. `<html lang>` updated dynamically on locale switch.
- R15. Accessibility floor — STYLING_SPECS §6 spec: AA target, `--ink-3` re-pointed to `--neutral-500` before sprint, `--primary-600` for body-size text (not `--primary-500`), focus-visible spec applied via delta patch, `<html lang="vi-VN">` default.
- R16. Responsive — 1024px minimum supported width per design note 08. Below 1024 → notice screen (bilingual). Operator role's 768px breakpoint deferred (Operator role not in slice).
- R17. Typography — IBM Plex Sans / IBM Plex Mono pinned. Update README + `index.html` to remove stale "Inter Tight" / "Inter" references. Self-host woff2 + subset to `vietnamese + latin-ext + latin`.
- R18. Logo SVG sprite — extract dot-matrix from prototype's `app.jsx` `<Sidebar>` into single `<Logo size={n}>` component, monochrome `currentColor`, used at 14px (top-bar), 64px (login).
- R19. Favicon set — generate 16/32/180/192/512 PNG + SVG from logo, ship in `web/public/`.

**Backend changes (in this sprint)**
- R20. Inventory.Api ships HTTP controllers exposing existing repository methods:
  - `GET /api/v1/inventory/skus?filter={cat,channel,state,zone,q}&page=…`
  - `GET /api/v1/inventory/skus/<sku>/ledger?cursor=…`
  - `POST /api/v1/inventory/skus` (create)
  - `POST /api/v1/inventory/adjustments`
  - `PUT /api/v1/inventory/skus/<sku>/threshold`
- R21. Inventory.Api ships **new** Backend Gap aggregate endpoint `GET /api/v1/inventory/summary` returning `{ total, reserved, below_threshold, oversell_risk }`.
- R22. Backend ships **stub Auth.Api module**: minimal `POST /auth/login` returning a hard-coded JWT (with tenant_slug claim matching tenant catalog seed). Marked clearly as dev-mode stub via sign-off documentation; Sprint-7 swaps real implementation.

**CI + repo wiring**
- R23. CI workflow `.github/workflows/ci.yml` adds frontend build job: `pnpm install` → `pnpm build` → `pnpm test`. Runs in parallel with existing dotnet jobs.
- R24. `web/` directory committed with frontend source; lockfile committed; `node_modules/` gitignored.

---

## Acceptance Examples

- AE1. **Covers R1, R3, R7, R13.** Given fresh `git clone` + `pnpm install` in `web/`, when developer runs `pnpm dev` and opens `localhost:5173`, then login screen appears with amber-ochre logo + form. Submits credentials → fake `/auth/login` returns JWT → redirects to `/inventory` → Inventory screen mounts with real SKU table data fetched from backend.

- AE2. **Covers R4, R6.** Given Owner is on Inventory screen, when they click a row, then drawer slides in from right (150ms ease-out per STYLING_SPECS), shows that SKU's reservation ledger with `running_balance` column, per-channel allocation bar at top. Esc closes drawer.

- AE3. **Covers R8, R10.** Given Owner clicks "Điều chỉnh tồn" in drawer, when they submit delta=+10 reason="recount", then POST `/api/v1/inventory/adjustments` fires with `Idempotency-Key: <ulid>` header. Backend writes `stock_adjustments` row. Within 2 seconds, polling re-fetches: SKU table row shows new on-hand value, drawer ledger shows new entry, toast confirms. Audit row exists in backend.

- AE4. **Covers R10.** Given Owner clicks flash-sale toggle in drawer, when toggle flips, then PUT `/api/v1/skus/<sku>/flag` fires immediately. On success → toggle remains green. On failure → toggle reverts + error toast displays idempotency key + trace ID for support.

- AE5. **Covers R11, R12.** Given Owner clicks Dashboard nav item, then route resolves to `/dashboard` and `<ComingSoon screen="dashboard" targetSprint={7}>` renders showing Dashboard icon + "Dashboard · Coming Sprint-7" + 1-paragraph blurb. No 404, no crash.

- AE6. **Covers R5, R21.** Given Owner is on Inventory screen, when page mounts, then KPI strip fetches from `GET /api/v1/inventory/summary` and displays `{ total: 1247, reserved: 89, below_threshold: 12, oversell_risk: 3 }` (example values). Numbers update every 2s with polling.

- AE7. **Covers R15.** When automated a11y test runs against any rendered screen, no `--ink-3`-on-white or `--primary-500`-on-white body text contrast failures present (AA 4.5:1). Focus-visible ring visible on Tab navigation.

- AE8. **Covers R23.** When PR is opened, CI runs `pnpm build` + dotnet build + dotnet test in parallel. PR cannot merge if frontend build fails.

---

## Success Criteria

- Owner reviewer (anyone with backend repo access) can `git clone` + `pnpm install --filter web` + `pnpm --filter web dev` and reach a working Inventory screen showing real backend data in < 5 minutes from cold.
- Inventory + Owner read + 4 writes are fully wired end-to-end against real backend (Inventory.Api + Sprint-5 SkuFlag endpoint); fake auth is the only deferred surface.
- Forensic-spine demo: clicking a SKU row opens a drawer that displays append-only reservation ledger entries with running balance — visible proof of Sprint-1-redux's hot-path correctness primitive at the UI layer.
- 8 other screens navigate to credible "Coming Sprint-X" placeholders; nav is fully visible; no broken routes.
- Sign-off doc + tag `v0.9.0-frontend-vertical-slice` ship; CHANGELOG + README + CLAUDE.md current-stage updated.
- Sprint-7 planner can read this sprint's sign-off + INTEGRATION_INTENT.md (unchanged) and start real-auth + SignalR work without re-asking product questions.

---

## Scope Boundaries

### Deferred for later (named follow-up sprints)

- **Real auth module** (login + JWT issuance + refresh token rotation + Redis denylist + TOTP MFA + per-tenant member store) — Sprint-7.
- **Real SignalR hub** + 14 event types per INTEGRATION §5 (replacing 2s polling) — Sprint-7.
- **8 other screens** — Dashboard (Sprint-7 candidate), Orders (Sprint-8), Channels (Sprint-9), Compliance (Sprint-9), Audit (Sprint-10), Onboarding (Sprint-7-shared), Settings (split across sprints), Tenants Admin (Sprint-10).
- **11 of 12 Backend Gaps** identified in INTEGRATION §1 — `/saas/health`, `/saga/funnel`, `/pickers/today`, `/metrics/fulfilment`, `/breaches/causes`, `/finance/today`, `/compliance/partner-readiness`, `/inventory/top`, `/channels/allocation-rules`, per-tenant user store, PDPA breach SLA tracker.
- **Inbound module UI surface** (PO list / GRN form / reconciliation tickets) — INTEGRATION §10 notable cut. Backend module exists since Sprint-2-redux. UI design pass + sprint TBD.
- **Operator role functionality** — entire role + mobile pick-wave UI + 768px breakpoint. Sprint-8+.
- **Sprint-5.5 scale-gate harness closure** — already deferred from Sprint-5; remains deferred while frontend phase runs.
- **Power-user shortcuts** — J/K table navigation, command palette body. Defer Sprint-7+.
- **Multi-tenant switcher in TopBar** (single user belongs to multiple tenants) — defer (Sprint-7 with real auth).

### Out of scope (rejected — not in this product's identity for v1)

- **Channel marketplace official logos** (Shopee / Lazada / TikTok Shop / Shopify) — license review required; monogram-with-brand-color pattern grounded in prototype works for v1. Defer indefinitely until legal review.
- **Dark mode** — Phase-3 work per STYLING_SPECS §9. Light-only intentionally.
- **PDF export** (compliance audit PDF, retention report PDF) — Phase-3.
- **OG / social card** for `about.html` — marketing artifact, not v1.
- **Email templates** for SendGrid (alert, invitation, sub-processor disclosure) — Phase-3 transactional layer.
- **Self-serve onboarding** — admin-only v1 per resolved open question #5.
- **PDF audit report** (Seller dashboard button) — defer Phase-3 per resolved open question #6.
- **Returns / RMA flow, bulk import CSVs** (except flash-sale flag bulk import which is Settings T3) — out of scope.
- **End-customer-facing screens** (order tracking, returns portal) — ShopFlow is a WMS, not a storefront.
- **Mobile-specific UI for non-Operator roles** — design note 08 says 1024px floor; intentional cut.
- **Browser support older than ES2020** — modern evergreen only.

---

## Key Decisions

- **Inventory + Owner vertical chosen over Dashboard / Compliance / Onboarding alternatives**: pragmatic match — Backend Gap closure lightest (Inventory module already has all required repository methods; only `/inventory/summary` aggregate is net-new); forensic-spine ledger drawer demo connects to Sprint-1-redux's correctness primitive; first UI surface for Sprint-5 StockSync's `SkuFlag` aggregate.

- **Read + 4 writes depth chosen over read-only minimal**: Owner-level CRUD demonstrates the full integration loop (auth header + idempotency-key + audit row + polling echo) on a single screen. Read-only would prove fetching but skip the trickier write semantics (idempotency, audit, optimistic UI).

- **Hybrid auth deferral (fake `/auth/login` + 2s polling) chosen over pre-sprint backend prereq**: continues Sprint-2.5 / 4.5 / 5.5 deferral cadence — closes Sprint-6 within 1.5-2 weeks while preserving the methodology pattern; Sprint-7 closure becomes a named follow-up with clear scope.

- **"Coming Sprint-X" placeholder stub chosen over hidden nav / locked screens / skeleton wireframes**: credible "not built yet" state with future-work named; sidebar nav remains fully visible (signals architecture is in place); shared `<ComingSoon>` component costs ~30min vs ~2-3 days for full skeleton.

- **Same-repo `web/` subdirectory chosen over separate repo**: STYLING_SPECS canon — solo dev + methodology emphasis + single commit history aligned with backend tag chain; `git subtree split` available if separation ever needed.

- **Design-ahead, build-incrementally pattern**: Full FE design handoff (INTEGRATION_INTENT.md + STYLING_SPECS.md + 16 source files = ~14k words + 16 files) serves as **canonical reference for ALL upcoming frontend sprints (6, 7, 8...)**, not just Sprint-6. Backend additions in Sprint-7+ build toward contracts already specified in INTEGRATION §1.1 / §1.2, minimizing FE↔BE contract drift. The full FE design is locked; backend builds incrementally toward it. This is the load-bearing decision that justifies the depth of the integration docs.

- **Tokens.css delta patch shipped in U1 as paste-in**: 30+ line CSS patch from STYLING_SPECS §3.3 ready-to-apply; ships in U1 of Sprint-6 to anchor styling before any screen-level work.

- **A11y contrast fixes shipped before sprint screen work**: `--ink-3` re-point + `--primary-600` body-text rule + `<html lang="vi-VN">` + focus-visible spec — all in U1 token foundation work, not chased per-screen.

- **Polling interval = 2s for tables**: tight enough to feel live; loose enough to not hammer backend during dev. Drawer ledger fetches on-open (no polling in drawer). Sprint-7 swaps polling for SignalR; UI components stay unchanged.

- **vi-VN primary locale; en-US toggle works but copy QA deferred**: existing `i18n.jsx` patterns reused; persist locale to `localStorage.shopflow_lang`. Full English copy review is a Sprint-7+ polish pass.

---

## Dependencies / Assumptions

- **Branch from `v0.8.0-methodology-writeup`** — fresh `feat/sprint-6-frontend-vertical-slice` branch.
- **Node.js + pnpm tooling on dev machine** — Node 20+ LTS; pnpm 9+. Not yet installed (dev machine has only .NET 8 SDK and no Node). Installation step in U1.
- **Inventory module repository methods exist and work** — Sprint-1-redux + Sprint-2-redux + Sprint-3-redux + Sprint-5 already shipped: `TryReserveAsync`, `AdjustAtBinAsync`, ledger query, etc. Sprint-6 only adds HTTP layer + 1 new aggregate endpoint.
- **StockSync `SkuFlag` PUT endpoint exists from Sprint-5 U7** — `PUT /api/v1/skus/<sku>/flag` is already in StockSync.Api. Sprint-6 frontend consumes it.
- **TenantRoutingMiddleware reads JWT `tenant_slug` claim** — Sprint-1-redux baseline. Fake auth issues JWT with this claim populated; middleware validates and routes to correct tenant DB.
- **Backend gap `GET /api/v1/inventory/summary` is the only net-new endpoint** — adds aggregate query over `stock_items` + `reservations_ledger`. Other 11 Backend Gaps remain unaddressed.
- **Design handoff at `D:\side_projects\Shopflow\design_handoff_shopflow_wms\` remains canonical** — INTEGRATION_INTENT.md + STYLING_SPECS.md unchanged; 16 source files (`app.jsx`, `components.jsx`, `screen-inventory.jsx`, `tokens.css`, etc.) are the visual specification.
- **`web/` subdirectory does not conflict with any backend file** — confirmed; only `tools/`, `src/`, `tests/`, `docs/`, `infrastructure/` exist at repo root.
- **CI runner has Node.js available** — GitHub Actions ubuntu-latest ships Node by default.

---

## Outstanding Questions

### Resolve Before Planning

*(none — all product decisions captured in this brainstorm + INTEGRATION_INTENT + STYLING_SPECS)*

### Deferred to Planning

- [Affects R1][Technical] Vite config specifics: SWC vs Babel? React Compiler RC?; library choice for client routing (TanStack Router vs React Router 6); state library (Zustand for global, React Query for server state seems natural — confirm in plan).
- [Affects R2][Technical] Token file layout: single `tokens.css` import from prototype or split into `tokens-core.css` + `tokens-semantic.css`? Reasonable defaults exist; plan decides.
- [Affects R3][Technical] Fake auth shape — minimal `Auth.Api` csproj as new module quartet (Domain + Application + Infrastructure + Api) or just inline endpoint in Gateway? Plan decides; smaller is better for stub.
- [Affects R7][Technical] Polling library — TanStack Query's `refetchInterval: 2000` is canonical and likely; plan confirms.
- [Affects R12][Technical] `<ComingSoon>` design pass — exact icon + copy + layout. STYLING_SPECS doesn't anchor this since it's new. Plan does a quick mock.
- [Affects R13][Technical] JWT validation library — `Microsoft.AspNetCore.Authentication.JwtBearer` per existing TenantRoutingMiddleware. Plan confirms scheme registration.
- [Affects R20][Technical] Controller pattern for Inventory.Api — MediatR commands/queries (Sprint-1-redux pattern from `AddShopFlowDefaults`) or direct repo invocation in controllers? Plan decides; MediatR is established.
- [Affects R23][Technical] CI job parallelism — separate dotnet + frontend jobs OR matrix? Plan decides based on existing `.github/workflows/ci.yml` shape.
- [Affects R24][Process] `.gitattributes` for CRLF normalisation — friction mode 6 from methodology writeup. Add now (alongside `web/` `.gitignore`) since this is the cheapest fix to the cheapest unfixed friction.

### Roadmap context (high-level — separate brainstorm/plan when reached)

- **Sprint-7** — real auth module + SignalR hub + Dashboard or Orders screen wired against them. Estimated 2 weeks.
- **Sprint-8** — Orders + Channels screens, full Operator role (768px breakpoint, mobile-first pick-wave UI). Estimated 2 weeks.
- **Sprint-9** — Compliance + Audit screens (RTBF modal + sub-processor list + audit-event drawer with JSON diff). Estimated 2 weeks.
- **Sprint-10** — Onboarding wizard + Settings tier IA + Tenants Admin. Estimated 2 weeks.
- **Sprint-5.5** — scale-gate harness closure for StockSync (from Sprint-5 deferral). Pre-existing follow-up; runs in parallel with frontend phase if capacity allows.
- **Phase-3 polish** — Inbound module UI design + implementation (INTEGRATION §10 notable cut), Sprint-11+ candidate.
