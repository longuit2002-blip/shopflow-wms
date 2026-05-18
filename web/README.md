# ShopFlow WMS — Web (Frontend)

React 19 + TypeScript + Vite frontend for **ShopFlow WMS**.

Sprint-6 ships a **vertical slice MVP**: Inventory screen × Owner role, end-to-end wired against backend (`Inventory.Api` + a fake `Auth.Api` stub). Eight other screens are `Coming Sprint-X` placeholders. See [`docs/plans/2026-05-18-002-feat-sprint-6-frontend-vertical-slice-plan.md`](../docs/plans/2026-05-18-002-feat-sprint-6-frontend-vertical-slice-plan.md) for scope.

## Stack

- **React 19** + **TypeScript 5.6** (strict mode)
- **Vite 6** with `@vitejs/plugin-react-swc` for fast HMR
- **TanStack Router** (file-based routing) — [docs](https://tanstack.com/router)
- **TanStack Query** — server state cache + `refetchInterval` polling (2 s) until SignalR lands in Sprint-7
- **Zustand** — minimal client state (auth token + tenant slug)
- **lucide-react** — icon set, 1.5 stroke width
- **Vitest** + **@testing-library/react** + **vitest-axe** — unit / component / a11y tests
- **ESLint** (flat config) + **Prettier** + **TypeScript strict**

## Quickstart

```bash
# Requires: Node 20+, pnpm 10+
pnpm install
pnpm dev          # http://localhost:5173, proxies /api + /auth → http://localhost:8080
```

In another terminal, run the backend Aspire AppHost:

```bash
# from repo root
dotnet run --project src/ShopFlow.AppHost
```

The Vite dev server proxies `/api/*` and `/auth/*` to the Aspire gateway on `localhost:8080`. JWT bearer + `X-Tenant-Slug` headers are attached by the shared `httpClient` (see `src/shared/httpClient.ts`, lands in U5).

## Commands

| Command | What it does |
|---|---|
| `pnpm dev` | Start Vite dev server with HMR |
| `pnpm build` | Type-check (`tsc -b`) + Vite production build to `dist/` |
| `pnpm preview` | Serve the production build locally |
| `pnpm test` | Run Vitest once (CI mode) |
| `pnpm test:watch` | Run Vitest in watch mode |
| `pnpm test:coverage` | Run Vitest with v8 coverage report |
| `pnpm lint` | ESLint check (no fix) |
| `pnpm typecheck` | `tsc -b --noEmit` only |
| `pnpm format` | Prettier write |

## Conventions

- **TypeScript**: strict mode + `noUncheckedIndexedAccess` + `noUnusedLocals` + `noUnusedParameters`. Path alias `@/*` → `./src/*`.
- **No `console.log`** in committed code — use a logger or remove. Hook will flag it.
- **File layout**: organize by feature, not by file type. `src/features/inventory/`, `src/features/auth/`, `src/shared/`, `src/components/ui/`.
- **A11y floor**: WCAG 2.1 AA. `jest-axe` runs on key components in test suite. Focus-visible spec from `STYLING_SPECS §6` must be honored.
- **Locale**: `vi-VN` is the prototype locale. VND with NBSP + `₫`, `dd/mm/yyyy`, 24 h. See `STYLING_SPECS §5`.
- **Min viewport**: 1024 × 720 desktop-first. Operator role gets 768 px iPad portrait in Sprint-7+. Below 1024: render a polite "desktop-only" notice (CSS-only).

## Design canon

The full Sprint-6 frontend design lives in [`D:\side_projects\Shopflow\design_handoff_shopflow_wms`](../../Shopflow/design_handoff_shopflow_wms) (out-of-repo path; ship-time copy is at [`design_handoff/`](../design_handoff/) — see project root). **`INTEGRATION_INTENT.md`** (backend contracts) and **`STYLING_SPECS.md`** (tokens, typography, motion, a11y, locale) are the contract reference for ALL frontend sprints; backend builds incrementally toward this locked design to minimize FE↔BE drift.

## Sprint-6 scope (TL;DR)

- ✅ Owner role login (fake `POST /auth/login` returns baked JWT — Sprint-7 swaps real)
- ✅ Inventory screen: SKU table + filter strip + KPI strip + reservation-ledger drawer
- ✅ 4 writes: adjust stock, set threshold, set flash-sale flag, create SKU
- ✅ Idempotency-Key header on every POST
- ✅ 2 s polling for stock updates (TanStack Query `refetchInterval`)
- ⏭ Sprint-7: real `Auth.Api` + SignalR `StockHub` + Ops Manager + Operator roles + 8 other screens

## Not in Sprint-6

- SignalR (deferred Sprint-7; 2 s polling stands in)
- Real `Auth.Api` (fake stub stands in)
- Ops Manager + Operator roles (Owner only)
- 8 other screens (Inbound, Outbound, Channel, Stock Sync, Settings, Onboarding, Dashboard, Reports) — all `Coming Sprint-X` placeholders
- Playwright / visual regression — Vitest + RTL + axe only
- i18n switcher — `vi-VN` hard-coded; English copy lives in component strings, language toggle is Phase-3
- Dark mode — Phase-3

## Tag

Sprint-6 completion → tag `v0.9.0-frontend-vertical-slice`.
