# Sprint-6 Resume Note — 2026-05-19 (updated after U10)

Branch: `feat/sprint-6-frontend-vertical-slice` (pushed to `origin`).
Cut from tag `v0.8.0-methodology-writeup`.

Plan file (source of truth): [`docs/plans/2026-05-18-002-feat-sprint-6-frontend-vertical-slice-plan.md`](./2026-05-18-002-feat-sprint-6-frontend-vertical-slice-plan.md)

## Where I stopped

**Through U10 inclusive shipped.** 4 units remain: U11, U12, U13, U14.

### Commit log (newest first, all on the branch)

```
0104dcd feat(sprint-6 U10): reservation-ledger drawer — Drawer primitive + AllocationBar + LedgerRow + LedgerDrawer; no-poll
c3916ba feat(sprint-6 U9): inventory screen — SKU table + filter strip + KPI strip + 2s polling
1ced521 feat(sprint-6 U8): Inventory.Api WRITE controllers — adjustments + threshold + flash-sale + create-sku
0589aca feat(sprint-6 U7): Inventory.Api READ controllers — skus list + ledger + summary
31276ae feat(sprint-6 U6): TanStack Router + 8 ComingSoon stub routes + auth guard
0da396b feat(sprint-6 U5): login screen + JWT storage + auth httpClient
af733c3 feat(sprint-6 U4): Auth.Api dev-mode stub + JwtBearer in Inventory.Api
c15638d feat(sprint-6 U3): app shell — TopBar + Sidebar + Logo + i18n + ComingSoon
4729348 feat(sprint-6 U2): token CSS layer + IBM Plex self-host + a11y fixes
b3bc591 feat(sprint-6 U1): web/ Vite + React 19 + TypeScript scaffold
```

### U10 additions (2026-05-19 session — fresh-machine resume)

- `web/src/components/primitives/Drawer.tsx` (+ test, 10 cases) — reusable right-side drawer; 150 ms slide-in; ARIA dialog; Esc/backdrop/X close; basic Tab focus trap.
- `web/src/components/inventory/AllocationBar.tsx` (+ test, 7 cases) — per-channel stacked bar; empty placeholder when `Allocations: []` (Sprint-6 trade-off #3).
- `web/src/components/inventory/LedgerRow.tsx` (+ test, 9 cases) — single ledger row; signed quantity; localized status pills.
- `web/src/components/inventory/LedgerDrawer.tsx` (+ test, 6 cases) — composes the above; `useSkuLedgerQuery` with **no polling** (U11 mutations invalidate; Sprint-7 SignalR push).
- `web/src/hooks/useInventoryQuery.ts` — flipped `useSkuLedgerQuery.refetchInterval` from `POLL_MS` to `false` (matches U10 spec; the plan called for a new `useLedgerQuery.ts` file but U7 had already consolidated the hook into `useInventoryQuery.ts`).
- `web/src/routes/_auth/inventory.tsx` — mounts `<LedgerDrawer>`; derives `selectedItem` from the inventory query result + the existing `selectedSku` state from U9.
- `web/src/tokens/tokens.css` — appended `@keyframes drawerSlideIn` + `@keyframes drawerMaskFadeIn` + `prefers-reduced-motion` override.

Vitest count: 78 → 111 (+33).

## Frontend state (`web/`)

- React 19 + TypeScript strict + Vite 5 (SWC) + TanStack Router (file-based) + TanStack Query (2 s polling) + Zustand.
- Self-hosted IBM Plex Sans + Plex Mono via `@fontsource` (vietnamese + latin-ext + latin subsets, weights 400/500/600/700).
- Design canon `tokens.css` ported with §3.3 delta inlined + §6.1 a11y fixes (`--ink-3` → `--neutral-500`).
- Routes: `/login` (LoginScreen → fake `/auth/login` → JWT in localStorage → navigate `/inventory`), `/_auth/*` guarded layout, `/inventory` real screen + 9 ComingSoon stubs.
- httpClient attaches `Authorization: Bearer` + `X-Tenant-Slug` + (for mutations) `Idempotency-Key: <ulid>` automatically. On 401 → logout + redirect.

### Verification (run from `web/`)

```bash
pnpm install      # node_modules/ is gitignored; first run on a fresh clone
pnpm typecheck    # tsr generate + tsc -b
pnpm test         # Vitest — 78 tests across 13 files, all green
pnpm lint         # ESLint flat config, 0 warnings
pnpm build        # tsc + vite build; emits dist/ with per-route chunks
pnpm dev          # http://localhost:5173, proxies /api + /auth → :8080
```

## Backend state

New module: `src/Services/Auth/ShopFlow.Auth.{Domain,Application,Infrastructure,Api}` — dev-mode fake `POST /auth/login` returning a baked JWT (`tenant_slug=yensaokhanhhoa`, `role=tenant_seller`). Wired into Aspire AppHost + Gateway `/auth/**` route.

`Inventory.Api` gains:

- `JwtBearer` authentication scheme reading the shared `Auth:DevSecret`.
- `SkusController @ /api/v1/inventory/skus` — GET (list + ledger), POST (create), PUT `/{sku}/threshold`, PUT `/{sku}/flash-sale`.
- `InventoryController @ /api/v1/inventory/summary` — KPI aggregate.
- `AdjustmentsController @ /api/v1/inventory/adjustments` — POST signed delta.

`Inventory.Application/Commands/` + `Inventory.Application/Queries/`: MediatR command + query records for all 7 endpoints (read + write).

`Inventory.Infrastructure`:
- `StockItemRepository.AdjustAsync` + `FindBySkuAsync` + `AddAsync` filled in (were NIE).
- `InMemorySkuMetadataStore` singleton holds `(threshold, isFlashSale)` per `(tenant_slug, sku)` — Sprint-7 promotes to real EF columns.

CPM: `Microsoft.IdentityModel.JsonWebTokens 8.2.1` added.

### Verification (CI-only)

This dev machine has .NET 8.0.407, the repo pins 9.0.305 via `global.json`. Backend builds + integration tests run in CI per the established Sprint-1..5 posture. On a laptop with the 9.0.305 SDK installed:

```bash
dotnet build                                          # 0 errors, 0 warnings expected
dotnet test --filter "Category!=Integration"          # adds 2 Auth.UnitTests cases
```

## What remains (~4 units, ~30 % of Sprint-6)

### U11 — Adjust stock modal + Set threshold inline edit

- Plan section: line 805.
- New: `web/src/components/primitives/Modal.tsx`, `web/src/components/inventory/AdjustStockModal.tsx`.
- Threshold inline edit: clickable cell in the SKU table → input → PUT `/threshold` on blur/Enter.
- Both use `useIdempotencyKey()` (already shipped in U5) so retries reuse one ULID.
- Backend already done: `POST /api/v1/inventory/adjustments` + `PUT /skus/{sku}/threshold`.

### U12 — Flash-sale toggle + Create SKU modal

- Plan section: line 857.
- Add a toggle column / cell action that calls `inventoryApi.setFlashSale(sku, active)`.
- Create-SKU modal opens from the FilterStrip's "Thêm SKU" CTA (already wired the prop in U9 — just pass an `onClick` from `inventory.tsx`).
- Backend already done: `POST /api/v1/inventory/skus` + `PUT /skus/{sku}/flash-sale`.

### U13 — CI workflow + frontend Vitest setup + RTL + jest-axe + smoke build

- Plan section: line 907.
- Add `.github/workflows/web-ci.yml`: pnpm install + typecheck + lint + test + build on every PR.
- Wire `vitest-axe` (already installed) into a smoke test that runs the rendered Inventory route through axe and asserts no violations.
- Update the main CI workflow at `.github/workflows/ci.yml` to add a `web-build` job alongside the dotnet jobs.

### U14 — Sign-off + CHANGELOG + README/CLAUDE update + tag v0.9.0-frontend-vertical-slice

- Plan section: line 971.
- Write `docs/phase-gates/2026-05-XX-sprint-6-signoff.md` following the Sprint-1..5 template (Goal / Surface shipped / Deviations / Verification / Trade-offs).
- Append to `docs/CHANGELOG.md`.
- Update `README.md` + `CLAUDE.md` "current stage" section to reflect Sprint-6 completion + Sprint-7 entry-point list.
- Tag `v0.9.0-frontend-vertical-slice` on the merge commit.

## How to resume on the laptop

```bash
git clone https://github.com/longuit2002-blip/shopflow-wms.git
cd shopflow-wms
git checkout feat/sprint-6-frontend-vertical-slice

# Frontend
cd web && pnpm install && pnpm test && pnpm build && cd ..

# Backend (requires .NET 9.0.305 per global.json — install if missing)
dotnet build
```

Then continue with `/ce-work docs/plans/2026-05-18-002-feat-sprint-6-frontend-vertical-slice-plan.md` and pick up at **U11**. The plan unit numbering matches my todo state — agent will infer "U1-U10 already shipped" from the commit log on the branch.

### Vercel agent-skills (installed 2026-05-19)

`.claude/skills/` ships 7 skills cloned from `vercel-labs/agent-skills`:
`vercel-react-best-practices`, `web-design-guidelines`, `vercel-composition-patterns`, `vercel-react-view-transitions`, `vercel-react-native-skills`, `deploy-to-vercel`, `vercel-cli-with-tokens`. The first three are the most relevant for U11-U14 frontend work; the last three (deploy/CLI/RN) are not directly applicable but kept for completeness. They auto-load on the next Claude Code session boot.

## Sprint-6 trade-offs locked in (carry into U10-U14)

These are documented inline in commit messages + code comments, but worth re-stating so the resume agent doesn't try to "fix" them:

1. **No new schema migration in Sprint-6.** Cosmetic columns (name, category, threshold, is_flash_sale, channel_alloc) wait for Sprint-7. Threshold + flash-sale stored in `InMemorySkuMetadataStore` singleton; values reset on `Inventory.Api` restart. Acceptable for demo loop.
2. **No `inventory_idempotency_records` table.** `Idempotency-Key` header is logged, not deduped server-side. Natural dedupe via `stock_adjustments` audit table.
3. **No cross-module joins.** Channel allocations + p24 outbound ship as empty/zero. Sprint-7 wires real Channel module integration.
4. **No URL-search-params persistence** for filter state — local React state only.
5. **Reservation ledger cursor pagination deferred** — drawer reads the most-recent N entries (default 100, clamped to 500).
6. **Wire shape is PascalCase** (.NET default serializer) — TS types mirror it exactly. Sprint-7 normalizes the kestrel JsonNamingPolicy to camelCase.
7. **Backend builds run in CI**, not on this dev machine (which has .NET 8.0.407 vs the repo's 9.0.305 pin). Same posture as Sprint-1..5.

## State preserved at session-end

- `pnpm install` was run; `web/node_modules/` is gitignored — re-run on the laptop.
- `web/src/routeTree.gen.ts` IS committed (TanStack Router's auto-generated file; intentionally tracked for IDE intellisense + CI determinism).
- No uncommitted changes; `git status` should be clean on the laptop after clone.
