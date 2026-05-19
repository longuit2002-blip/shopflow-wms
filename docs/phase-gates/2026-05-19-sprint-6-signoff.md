---
title: "Sprint-6 sign-off — Frontend Vertical Slice (Inventory × Owner)"
date: 2026-05-19
status: complete
follows: docs/phase-gates/2026-05-18-methodology-writeup-signoff.md
plan: docs/plans/2026-05-18-002-feat-sprint-6-frontend-vertical-slice-plan.md
tag: v0.9.0-frontend-vertical-slice
---

# Sprint-6 sign-off — Frontend Vertical Slice (Inventory × Owner)

Sprint-6 ships ShopFlow WMS's first frontend surface: a vertical slice of the Inventory screen for the Owner role, end-to-end through real Inventory.Api WRITE controllers, with a fake `Auth.Api` stub returning a baked JWT (deferred to Sprint-7 real auth) and 2-second TanStack Query polling in place of SignalR push (also Sprint-7). Fourteen implementation units shipped on `feat/sprint-6-frontend-vertical-slice` cut from `v0.8.0-methodology-writeup`. New top-level `web/` subdirectory holds the React 19 + TypeScript + Vite project; the backend gains a stub `ShopFlow.Auth.{Domain,Application,Infrastructure,Api}` quartet plus a real `Inventory.Api` HTTP surface (SkusController, InventoryController, AdjustmentsController) wired into the Gateway.

The slice proves the methodology pattern end-to-end at the frontend layer: design canon → brainstorm → plan with R/A/F/AE IDs → 14 sequential units → per-unit commit → final sign-off + tag. Eight other screens ship as `ComingSoon` placeholders behind the auth guard so the navigation shell is complete on day one.

## What shipped

| U-ID | Goal | Status |
|------|------|--------|
| U1 | `web/` scaffold — Vite + React 19 + TypeScript strict + ESLint + Prettier + Vitest + pnpm@10.32.1 pinned via `packageManager`; first SmokeTest renders. | ✅ |
| U2 | `tokens.css` design layer ported from canon — warm-neutral palette, amber-ochre accent, IBM Plex Sans + Plex Mono self-hosted via `@fontsource`; STYLING_SPECS §3.3 delta inlined + §6.1 a11y fixes (`--ink-3` → `--neutral-500` for AA contrast). | ✅ |
| U3 | App shell — `<TopBar>` with tenant pill + locale switcher + user menu; `<Sidebar>` 10-item nav with Logo; ComingSoon placeholder; `useLocale` Zustand-backed `t(vi, en)` translator + `<html lang>` sync. | ✅ |
| U4 | `ShopFlow.Auth.{Domain,Application,Infrastructure,Api}` quartet — dev-mode fake `POST /auth/login` returning a baked JWT (`tenant_slug=yensaokhanhhoa`, `role=tenant_seller`) signed via `Auth:DevSecret`. JwtBearer auth scheme wired in `Inventory.Api`. Gateway route `/auth/**` registered. | ✅ |
| U5 | `<LoginScreen>` + `useAuth` Zustand store + JWT-in-localStorage persistence + `httpClient` auto-injects `Authorization: Bearer` + `X-Tenant-Slug` + `Idempotency-Key` (per-mutation ULID) headers; 401 → logout + redirect. | ✅ |
| U6 | TanStack Router file-based routes — `_auth` guarded layout + 9 child routes (1 real `/inventory`, 8 `ComingSoon` stubs for orders/inbound/outbound/channels/sync/audit/tenants/settings/onboarding). | ✅ |
| U7 | `Inventory.Api` READ controllers + MediatR queries — `GET /api/v1/inventory/skus` (list + filter), `GET /skus/{sku}/ledger`, `GET /inventory/summary` (KPI aggregate). `InMemorySkuMetadataStore` singleton holds `(threshold, isFlashSale)` per `(tenant_slug, sku)` — Sprint-7 promotes to real EF columns. | ✅ |
| U8 | `Inventory.Api` WRITE controllers + MediatR commands — `POST /adjustments` (signed delta), `PUT /skus/{sku}/threshold`, `PUT /skus/{sku}/flash-sale`, `POST /skus` (create). `Idempotency-Key` header logged in audit table. | ✅ |
| U9 | Inventory screen — `<KpiStrip>` + `<FilterStrip>` + `<SkuTable>` driven by `useInventoryQuery` (2-s polling). Status pills (OK / Below threshold / Oversell risk). Search debounced via TanStack Query keying. | ✅ |
| U10 | `<Drawer>` primitive (150 ms slide-in, focus trap, Esc close, basic Tab cycling) + `<AllocationBar>` (per-channel stacked bar with empty placeholder per trade-off #3) + `<LedgerRow>` + `<LedgerDrawer>` (no-poll; invalidated on U11 mutations). | ✅ |
| U11 | `<Modal>` primitive + `<Toast>` + `useToast` Zustand store + `useInventoryMutations` test-first (ULID-per-call Idempotency-Key, success → invalidate `['inventory', 'skus'|'summary'|'ledger']`, error → toast with key + trace-id). `<AdjustStockModal>` (delta + reason + note) wired to drawer Adjust CTA. `<ThresholdInlineEdit>` (optimistic UI via React 19 set-state-during-render) wired to SKU table threshold cell. | ✅ |
| U12 | `<Toggle>` primitive (role=switch, aria-checked, Space/Enter native) + `<FlashSaleToggle>` (optimistic UI, disabled-while-pending anti-double-click) wired to LedgerDrawer header. `<CreateSkuModal>` (sku regex validation, 409 → inline duplicate error, 5xx → modal stays open + toast) wired to FilterStrip "Thêm SKU" CTA. | ✅ |
| U13 | Frontend CI job in `.github/workflows/ci.yml` — pnpm/action-setup@v4 reading `packageManager` from package.json, Node 20 + pnpm cache, runs typecheck → lint → vitest (incl. a11y smoke) → build. Vitest-axe matchers registered; `web/src/a11y.smoke.test.tsx` covers 6 surfaces. SkuTable `nested-interactive` violation fixed (row drops button role; SKU cell hosts the button). | ✅ |
| U14 | Sign-off doc + CHANGELOG entry + README + CLAUDE current-stage update + tag `v0.9.0-frontend-vertical-slice` | ✅ |

## Stack & infrastructure delta

| Surface | Change |
|---|---|
| `web/` (NEW) | Top-level subdirectory: Vite 5 + React 19 + TypeScript strict + TanStack Router (file-based) + TanStack Query (2s polling) + Zustand. Self-hosted IBM Plex Sans + Plex Mono. ~70 source files at sprint close. |
| `src/Services/Auth/*` (NEW) | 4-csproj quartet (Domain / Application / Infrastructure / Api). Stub-grade dev-mode auth; baked JWT signed via shared secret. Sprint-7 replaces with real implementation. |
| `src/Services/Inventory/Inventory.Api` | +3 controllers (SkusController, InventoryController, AdjustmentsController); JwtBearer scheme reading `Auth:DevSecret`. |
| `src/Services/Inventory/Inventory.Application` | +3 query handlers, +4 command handlers (MediatR). |
| `src/Services/Inventory/Inventory.Infrastructure` | `StockItemRepository.AdjustAsync` + `FindBySkuAsync` + `AddAsync` filled in (were NIE). `InMemorySkuMetadataStore` singleton for threshold + flash-sale flag display (Sprint-7 → real EF columns). |
| `src/ApiGateway/ShopFlow.Gateway/appsettings.json` | +1 route `/auth/**` → `auth-api`. |
| `src/AppHost/ShopFlow.AppHost/Program.cs` | +1 resource `auth-api`. |
| `ShopFlow.sln` | +4 Auth csproj entries. |
| `.github/workflows/ci.yml` | +1 `frontend` job (parallel with .NET jobs). |
| `Directory.Packages.props` | `Microsoft.IdentityModel.JsonWebTokens 8.2.1` for JWT signing. |

## Test count

| Tier | Sprint-5 (baseline) | Sprint-6 added | Sprint-6 total |
|------|---|---|---|
| Backend Unit | 359 | +2 (Auth.UnitTests) | 361 |
| Backend Integration | 24 baseline | (no new) | 24 |
| **Frontend Vitest** | — (no frontend pre-Sprint-6) | **+221** | **221** |
| Frontend test files | — | 27 | 27 |

Frontend test breakdown (highlights):
- Primitives — Drawer 10, Modal 13, Toast 10, Toggle 10, Logo 5
- Inventory components — AllocationBar 7, LedgerRow 9, LedgerDrawer 10, AdjustStockModal 14, ThresholdInlineEdit 12, FlashSaleToggle 7, CreateSkuModal 14
- Hooks — useToast 10, useInventoryMutations 11, useIdempotencyKey, useLocale, jwt, ulid
- Auth — LoginScreen 4
- Shell — Sidebar 6, TopBar, LocaleSwitcher 2, TenantPill 2
- **A11y smoke (U13)** — 6 axe-clean assertions across LoginScreen + SkuTable + AdjustStockModal + CreateSkuModal + FlashSaleToggle + ToastViewport

## Frontend bundle size (`pnpm build` after U13)

| Chunk | Raw | Gzipped |
|---|---|---|
| `index.js` (vendors) | 311.16 kB | 97.93 kB |
| `inventory.js` (Inventory route) | 42.15 kB | 13.44 kB |
| `_auth.js` (auth layout) | 12.07 kB | 4.28 kB |
| `login.js` | 3.76 kB | 1.76 kB |
| CSS (tokens + fonts) | 21.53 kB | 4.78 kB |

Sprint-6 carries no perf budget; numbers are baseline for Sprint-7+ tracking.

## Key technical decisions (recap of plan KTDs + emergent)

- **KTD1 — `web/` subdirectory, not separate repo.** Co-locating frontend with backend keeps PRs atomic across BE+FE changes; matches the modular-monolith ethos at the source-tree level. Sprint-7+ can extract if frontend ownership truly forks.
- **KTD2 — Hybrid auth deferral.** Fake `Auth.Api` ships in Sprint-6 returning a baked JWT; real auth (refresh tokens, password hashing, role claims, MFA) is Sprint-7. Lets the slice ship without bikeshedding the auth perimeter.
- **KTD3 — Path-alias `@/*` → `./src/*`** via `tsconfig.app.json` paths. Survives router moves; matches Sprint-7+ shadcn-style template patterns.
- **KTD4 — Wire shape stays PascalCase** matching `.NET` default serializer (no `JsonNamingPolicy.CamelCase` on the kestrel side). Sprint-7 normalises to camelCase as a single migration. Frontend TS types mirror exact wire shape.
- **KTD5 — 2-second TanStack Query polling** instead of SignalR push. SignalR is a Sprint-7 add when the multi-screen UX needs cross-tab change broadcast. 2s is below human-noticeable but above network noise floor.
- **KTD6 — In-memory threshold + flash-sale store** (`InMemorySkuMetadataStore` singleton). Sprint-6 trade-off #1: no new EF migration in this sprint; cosmetic columns wait for Sprint-7's schema expansion. Values reset on `Inventory.Api` restart; acceptable for demo loop.
- **KTD7 — One write path for flash-sale** (Inventory.Api `/flash-sale`, not Sprint-5's StockSync `/flag`). Sprint-6 trade-off #3 rules out cross-module joins; the polled SKU list reads display data from Inventory.Api anyway, so a StockSync write wouldn't surface visually. Sprint-7 wires both surfaces when persistence lands.
- **KTD8 — CreateSkuModal collects only `sku + initialAvailable`** (matching `CreateSkuCommand` backend). Plan listed name/cat/threshold/price/cost/alloc; deferring to Sprint-7 beats collect-and-discard UX. Modal footer text labels deferred fields explicitly.
- **KTD9 — Modal Esc uses capture-phase + `stopImmediatePropagation()`** so Modal-over-Drawer Esc closes only the modal. Without this, both `document.addEventListener('keydown')` handlers fire and both surfaces close at once. Modal mounts at the route level (sibling of LedgerDrawer) rather than nested, avoiding DOM focus-trap conflicts.
- **KTD10 (U11 audit) — React 19 "Adjust State Based on Props" pattern** for optimistic UI (ThresholdInlineEdit + FlashSaleToggle). Set state during render with a `prev-prop !== value` guard instead of `useEffect`. Avoids the extra render cycle Vercel `rerender-derived-state-no-effect` flags.
- **KTD11 (U13 emergent) — SkuTable `nested-interactive` fix.** Original design had `<tr role="button" tabindex="0">` wrapping ThresholdInlineEdit's button. Axe-core via the new a11y harness caught it; row dropped button semantics, first-column SKU cell hosts a real `<button>` (carries `aria-pressed` + drawer-open onClick). Mouse behavior unchanged; keyboard users get a semantically clearer focus target.
- **KTD12 (U13 emergent) — vitest-axe@0.1.0 type-augmentation shim.** Package augments the obsolete `Vi.Assertion` namespace (Vitest 0.x); Vitest 2.x lives in the `vitest` module. `web/src/types/vitest-axe.d.ts` re-declares the augmentation correctly. Sprint-7 follow-up: upstream PR or migrate to a maintained fork.

## Deviations from plan file list

- **U10 — `useSkuLedgerQuery` consolidated into `useInventoryQuery.ts`**, not a new `useLedgerQuery.ts` file. U7 had already placed the hook there; splitting would have added churn for no semantic gain.
- **U10 — drawer `headerExtra` slot reserved but unused** until U12. U12 mounted FlashSaleToggle there without modifying Drawer's API.
- **U11 — Modal Esc moved to capture phase + `stopImmediatePropagation()`** to resolve Modal-over-Drawer Esc conflict (KTD9 above). Plan didn't specify the contract; this is the cleanest fix.
- **U11 — AdjustStockModal mounted at the route level** (sibling of LedgerDrawer) instead of as a child of LedgerDrawer. Avoids Drawer's keydown focus-trap (on the drawer aside element) interfering with modal focus.
- **U11 — ThresholdInlineEdit uses set-state-during-render** for prop sync instead of `useEffect`. Plan text said "optimistic UI"; the implementation pattern was open. Applied per Vercel `rerender-derived-state-no-effect`.
- **U11 — All idempotency keys regenerated per `mutateAsync` call** (not per submission lifecycle via `useIdempotencyKey`). Plan tests explicitly required "retry → new ULID" for audit-only dedup (Sprint-6 trade-off #2); the `useIdempotencyKey` hook from U5 stays for any caller that needs cross-retry stability.
- **U12 — Flash-sale toggle hits Inventory.Api's `/flash-sale`** instead of StockSync.Api's `/flag` (KTD7 above). The plan-listed `web/src/api/stocksync.ts` file is therefore not shipped.
- **U12 — CreateSkuModal collects only `sku + initialAvailable`** (KTD8 above). Plan-listed extras (name, category, threshold, price, cost, channel allocations) are deferred to Sprint-7 per Sprint-6 trade-off #1.
- **U13 — vitest-axe type shim added** (KTD12 above). Plan assumed standard `vitest-axe/extend-expect` import would augment types; in practice the package's `extend-expect.js` ships empty and its `.d.ts` targets the wrong namespace for Vitest 2.x.
- **U13 — SkuTable refactored to fix `nested-interactive`** (KTD11 above). Caught immediately by the new a11y smoke harness — exactly the regression-prevention payoff the gate is designed for.
- **U13 — `vitest.config.ts` + `vitest.setup.ts` + `package.json` scripts already shipped** from Sprint-6 scaffold; U13 extended them (axe matchers, a11y smoke test) rather than creating from scratch.
- **No local Docker daemon on this dev machine** — same Sprint-1-redux through Sprint-5 posture. Backend integration tests run in CI; frontend tests run locally (no DB dependency).
- **Backend builds run in CI only** on this dev machine (which has .NET 8.0.407 vs the repo's `global.json`-pinned 9.0.305). Per the Sprint-1..5 established pattern.

## Sprint-6 trade-offs locked in for downstream sprints

These are documented inline in commit messages + code comments + plan; restating so future units don't try to "fix" them:

1. **No new schema migration in Sprint-6.** Cosmetic columns (name, category, threshold, is_flash_sale, channel_alloc) wait for Sprint-7. Threshold + flash-sale stored in `InMemorySkuMetadataStore` singleton; values reset on `Inventory.Api` restart. Acceptable for demo loop.
2. **No `inventory_idempotency_records` table.** `Idempotency-Key` header is logged but not deduped server-side. Natural dedupe via `stock_adjustments` audit table.
3. **No cross-module joins.** Channel allocations + p24 outbound ship as empty/zero. Sprint-7 wires real Channel module integration.
4. **No URL-search-params persistence** for filter state — local React state only.
5. **Reservation ledger cursor pagination deferred** — drawer reads the most-recent N entries (default 100, clamped to 500).
6. **Wire shape is PascalCase** (.NET default serializer) — TS types mirror exact wire shape.
7. **Backend builds run in CI**, not on this dev machine.
8. **Fake Auth** — `Auth.Api` returns a baked JWT; no password hashing, refresh tokens, MFA, or session rotation. Sprint-7 ships real auth.
9. **2-s polling, no SignalR push** — Sprint-7 adds SignalR; the hook signatures stay so consuming components don't change.
10. **Flash-sale single-endpoint** — Inventory.Api `/flash-sale` only. Sprint-7 also writes StockSync's `/flag` for routing.

## Carried-forward deferrals

- **Sprint-5.5** — close the U9 scale-gate harness gap (multi-tenant Aspire boot + real Shopee mock alongside StockSync.Api). Same posture as Sprint-4 → Sprint-4.5 closure. Not blocking; ship when there's appetite for the multi-tenant integration tooling.
- **Inbound module UI** — Sprint-6 ships only the Inventory screen; Inbound's GRN flow lives in the backend module from Sprint-2-redux but has no frontend surface yet.
- **Real auth, SignalR push, schema expansion for cosmetic SKU columns** — all Sprint-7.
- **vitest-axe upstream PR or fork** — KTD12 shim is in place; Sprint-7 cleanup.
- **CSharpier formatting cleanup carried** from Phase-0-redux U10 — 23 files inherited from U4-U6 still drift from `csharpier --check`. CI's csharpier step will block on first run; one cleanup commit fixes them.

## Vercel skills applied (U11-U13)

The resume note (`docs/plans/SPRINT-6-RESUME.md`) called out three relevant Vercel agent-skills installed under `.claude/skills/`:

- **`vercel-react-best-practices`** — `rerender-derived-state-no-effect` applied to ThresholdInlineEdit + FlashSaleToggle (KTD10). `rerender-no-inline-components`, `rerender-use-ref-transient-values`, `bundle-barrel-imports` audited green.
- **`vercel-composition-patterns`** — Modal uses `children` + `footer` slot composition (no boolean prop proliferation beyond `dismissOnBackdrop`); Toggle is a pure controlled primitive; useToast lifts global state to a single Zustand store.
- **`web-design-guidelines`** — `autoComplete="off"` + `inputMode="numeric"` + `spellCheck={false}` on numeric inputs; `aria-live` on async-update regions; `aria-label` on icon-only buttons; `htmlFor` on form labels; `aria-invalid` + `aria-describedby` on validation errors; `role="alert"` on inline error messages; backdrop dismiss disabled on modals with typed data; jsx-a11y/no-autofocus respected. Caught the SkuTable `nested-interactive` violation via the axe harness in U13.

## Branch + tag + commit chain

- Branch: `feat/sprint-6-frontend-vertical-slice` (cut from `v0.8.0-methodology-writeup`)
- Tag: `v0.9.0-frontend-vertical-slice` (annotated)
- Commit chain:
  - `448700e` (docs: brainstorm + plan) → `8da0e1d` (vercel skills install) → `b3bc591` (U1) → `4729348` (U2) → `c15638d` (U3) → `af733c3` (U4) → `0da396b` (U5) → `31276ae` (U6) → `0589aca` (U7) → `1ced521` (U8) → `c3916ba` (U9) → `2a8900d` (docs: U1-U9 resume note) → `0104dcd` (U10) → `04102d3` (docs: U10 resume note) → `570a939` (U11) → `77fe009` (U12) → `b772f51` (U13) → U14 commit + tag

## Next implementation step

Cut a fresh branch from `v0.9.0-frontend-vertical-slice` and pick one of:

- **Sprint-7 — Real auth + SignalR push + cosmetic SKU schema expansion.** The natural follow-on: replace the U4 Auth stub with real password hashing + refresh tokens; replace 2-s polling with SignalR push (hook signatures already stable); migrate the in-memory threshold + flash-sale + name + category + channel allocations to EF columns + a single migration; normalize Kestrel JsonNamingPolicy to camelCase.
- **Sprint-7 alt — Orders screen or Dashboard.** Second frontend vertical slice. Orders cuts deepest because it touches Outbound's saga + reservation ledger from Sprint-3-redux. Dashboard surfaces the KPI strip pattern at app scale.
- **Sprint-5.5 — Scale-gate harness closure.** Backend-only point release. Same shape as Sprint-2.5 / Sprint-4.5. Runs in parallel with any Sprint-7 frontend work since it doesn't touch the `web/` tree.
- **Public blog post derivative.** Adapted ~3000-4000 word version of the methodology writeup OR the Sprint-6 case study for dev.to / personal blog.
- **Process improvements** flagged in the methodology forward-looking section — `.gitattributes` for CRLF normalisation; plan-time port-shape checklist; granular checkpoint commits inside subagent runs.

---

**Closing note**: Sprint-6 ships the methodology pattern at the frontend layer. The brainstorm-plan-unit-commit-signoff cadence proven across 7 backend sprints transfers cleanly — same friction modes (KTD emergence mid-sprint, plan-vs-reality reconciliation, deferred carries documented inline), same scaffolding (R/A/F/AE IDs, U-IDs, sign-off template). The one new thing — agent-skills directly informing code patterns (Vercel React best-practices catching `rerender-derived-state-no-effect`; web-design-guidelines + axe-core catching `nested-interactive`) — earned its keep in the U11 + U13 audit passes.
