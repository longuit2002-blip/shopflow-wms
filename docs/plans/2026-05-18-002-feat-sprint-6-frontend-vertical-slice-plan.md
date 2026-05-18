---
title: "feat: Sprint-6 Frontend Vertical Slice MVP (Inventory × Owner)"
type: feat
status: active
date: 2026-05-18
origin: docs/brainstorms/2026-05-18-sprint-6-frontend-vertical-slice-requirements.md
follows: docs/phase-gates/2026-05-18-methodology-writeup-signoff.md
tag_target: v0.9.0-frontend-vertical-slice
---

# feat: Sprint-6 Frontend Vertical Slice MVP (Inventory × Owner)

## Overview

Ship the first frontend vertical slice in `web/` subdirectory using React + TypeScript + Vite + token-based CSS. Inventory screen × Owner role end-to-end với 4 writes (adjust / threshold / flash-sale / create SKU), Reservation Ledger drawer (forensic-spine demo from Sprint-1-redux), 8 other screens là "Coming Sprint-X" placeholders. Hybrid auth deferral: fake `Auth.Api` stub module returning baked JWT + 2-second TanStack Query polling thay SignalR (Sprint-7 swaps real). 14 implementation units following Sprint-3/4/5-redux cadence; tag `v0.9.0-frontend-vertical-slice`.

Cut branch `feat/sprint-6-frontend-vertical-slice` từ `v0.8.0-methodology-writeup`.

---

## Problem Frame

Backend đã ship 7 sprints + Phase-3 methodology writeup. 6 modules ship complete (ControlPlane, Inventory, Inbound, Outbound, Channel, StockSync); business logic — atomic reservation correctness, fulfillment saga, channel ingress/egress — đã proven at backend test layer. **Project chưa có frontend.**

Design handoff đã land tại `D:\side_projects\Shopflow\design_handoff_shopflow_wms\` (16 source files + INTEGRATION_INTENT.md ~7350 words + STYLING_SPECS.md ~6800 words). Design serves as canonical contract reference cho TẤT CẢ upcoming frontend sprints (6, 7, 8...). "Design-ahead, build-incrementally" pattern minimizes FE↔BE drift.

Without Sprint-6: 7 sprints of backend correctness has no user-facing demonstration; wedge claim ("DB-per-tenant surfaced as product feature") has no UI surface; portfolio narrative depends entirely on README + code reading.

Sprint-6 starts the multi-sprint frontend phase. Vertical slice MVP is proof-of-integration; subsequent sprints expand to full 5-screen × 3-role surface.

---

## Requirements Trace

Origin requirements ([docs/brainstorms/2026-05-18-sprint-6-frontend-vertical-slice-requirements.md](../brainstorms/2026-05-18-sprint-6-frontend-vertical-slice-requirements.md)) → U-IDs:

| R-ID | Requirement | Owning U-IDs |
|---|---|---|
| R1 | Scaffold React + TypeScript + Vite + pnpm in `web/` | U1 |
| R2 | Token CSS extracted from prototype + STYLING_SPECS §3.3 delta patch | U2 |
| R3 | Login screen + fake `/auth/login` + JWT issuance | U4, U5 |
| R4 | SKU table với all columns + filter strip server-roundtrip | U7, U9 |
| R5 | KPI strip + new `GET /api/v1/inventory/summary` Backend Gap | U7, U9 |
| R6 | Reservation Ledger drawer với running_balance + allocation bar | U7, U10 |
| R7 | 2-second TanStack Query polling | U9, U10 |
| R8 | Adjust stock (modal + Idempotency-Key header) | U8, U11 |
| R9 | Set threshold (PUT + Idempotency-Key) | U8, U11 |
| R10 | Toggle is_flash_sale (PUT /skus/{sku}/flag — Sprint-5 U7 existing) | U12 |
| R11 | Create SKU (POST modal) | U8, U12 |
| R12 | `<ComingSoon>` shared component + 8 stub routes | U6 |
| R13 | JWT bearer + `X-Tenant-Slug` headers on every API call | U5, U6 |
| R14 | Locale vi-VN default + en-US toggle + localStorage persistence | U3 |
| R15 | A11y floor — `--ink-3` re-point, `--primary-600` body text, `<html lang="vi-VN">`, focus-visible | U2 |
| R16 | 1024px min responsive support; <1024 notice screen | U3 |
| R17 | IBM Plex Sans/Mono pinned; README + index.html cleanup; self-host woff2 subset | U2 |
| R18 | Logo SVG sprite extracted from dot-matrix in prototype `app.jsx` | U3 |
| R19 | Favicon set (16/32/180/192/512 PNG + SVG) | U3 |
| R20 | Inventory.Api HTTP controllers (5 endpoints surface existing repos) | U7, U8 |
| R21 | `GET /api/v1/inventory/summary` new aggregate endpoint (Backend Gap) | U7 |
| R22 | Backend Auth.Api stub module (fake login returning baked JWT) | U4 |
| R23 | CI `.github/workflows/ci.yml` adds frontend build job (parallel) | U13 |
| R24 | `web/` committed + lockfile + `.gitattributes` for CRLF normalisation + `node_modules/` gitignored | U1, U13 |

Acceptance Examples AE1-AE8 carry forward as test scenarios distributed across U5, U7, U9, U10, U11, U12, U13.

---

## Scope Boundaries

### In scope

- Tất cả R1-R24 per origin doc.
- Branch `feat/sprint-6-frontend-vertical-slice` cut từ `v0.8.0-methodology-writeup`.
- Sprint sign-off doc + CHANGELOG entry + README/CLAUDE.md current-stage update.
- Annotated tag `v0.9.0-frontend-vertical-slice`.

### Deferred to Follow-Up Work

- **Sprint-7 — real auth module** (login + JWT issuance + refresh token rotation + Redis denylist + TOTP MFA + per-tenant member store) swap of fake Auth.Api stub.
- **Sprint-7 — real SignalR hub** + 14 event types per INTEGRATION §5 (replace 2s polling).
- **Sprint-7+ — 8 other screens** progressively unlock — Dashboard, Orders, Channels, Compliance, Audit, Onboarding, Settings, Tenants Admin.
- **11 of 12 Backend Gaps** from INTEGRATION §1.1 — defer until consuming screen ships.
- **Inbound module UI** — Sprint-11+ candidate per INTEGRATION §10 notable cut.
- **Operator role** — entire role + mobile pick-wave UI + 768px breakpoint. Sprint-8+.
- **Multi-tenant switcher in TopBar** — Sprint-7 with real auth.
- **Playwright E2E suite + visual regression tests** — defer Sprint-7+ (Vitest + RTL unit/component tests only for Sprint-6).
- **Storybook component catalog** — defer; not yet needed at 1-screen scale.
- **Power-user shortcuts** (J/K table nav, command palette body) — Sprint-7+.

### Out of scope (rejected)

- **Channel marketplace official logos** — license review required; monogram-color pattern works.
- **Dark mode** — STYLING_SPECS §9 Phase-3.
- **PDF export, OG card, email templates** — Phase-3.
- **Self-serve onboarding, PDF audit report** — resolved open questions #5 + #6.
- **Returns/RMA, bulk import CSVs** (except flash-sale bulk in Settings T3).
- **End-customer-facing screens** — ShopFlow is a WMS, not a storefront.
- **Browser support older than ES2020** — modern evergreen only.
- **Full English locale copy QA** — en-US toggle works but copy review deferred.

---

## Key Technical Decisions

### KTD1 — React 19 + TypeScript 5 + Vite 6 (SWC) + TanStack Router/Query + Zustand

**Decision.** Stack: React 19 stable + TypeScript ~5.6 + Vite 6 with SWC plugin + TanStack Router (file-based routing, TypeScript-first) + TanStack Query (server state, polling via `refetchInterval`) + Zustand (minimal client state only — locale, drawer open/close).

**Rationale.** STYLING_SPECS + INTEGRATION_INTENT recommend "React + TypeScript + Vite + token-based CSS". TanStack Router beats React Router 6 on TypeScript ergonomics + the route file structure mirrors the sidebar nav cleanly. TanStack Query handles polling natively (`refetchInterval: 2000`) and Sprint-7 swap to SignalR replaces the query function while keeping component API identical. Zustand is small and fits the modest client-state surface; Redux/Jotai would be over-engineering at 1 screen. SWC is faster than Babel at scale and Vite 6 ships it as default plugin option.

### KTD2 — Backend `ShopFlow.Auth.Api` as full module quartet (stub-grade)

**Decision.** New `src/Services/Auth/ShopFlow.Auth.{Domain,Application,Infrastructure,Api}/` module quartet matching Sprint-1..5 cadence. Sprint-6 ships fake `POST /auth/login` returning baked JWT signed with dev-only HMAC secret + `JwtBearerDefaults.AuthenticationScheme` registered in Inventory.Api. Sprint-7 swaps real implementation without restructuring.

**Rationale.** Methodology pattern: every backend module is a 4-csproj quartet. Resisting the urge to ship "minimal inline endpoint" preserves the structural invariant — Sprint-7's real auth lands as a body-fill not a refactor. Auth.Application has zero handlers in Sprint-6 (only the controller layer matters); Sprint-7 adds login command, refresh handler, TOTP verification.

### KTD3 — Frontend repo layout: feature-folder hybrid

**Decision.** `web/src/` shape:

```
web/src/
├── routes/                  TanStack Router file-based — login.tsx, inventory.tsx, _coming-soon/dashboard.tsx, etc.
├── components/
│   ├── primitives/          Button, Pill, KPICard, Drawer, Modal — reuse across screens
│   ├── inventory/           SkuTable, LedgerDrawer, AdjustStockModal, CreateSkuModal, FlashSaleToggle
│   ├── shell/               TopBar, Sidebar, Logo, TenantPill
│   └── stubs/               ComingSoon component
├── hooks/                   useAuth, useTenant, useInventoryQuery, useLocale
├── api/                     httpClient (fetch wrapper), endpoints typed against Inventory.Api shapes
├── locales/                 vi-VN.ts, en-US.ts — port từ i18n.jsx
├── tokens/                  tokens.css (from prototype + delta patch) + fonts/
├── types/                   shared types for API contracts
└── lib/                     small utilities — fmtVND, fmtAge, slugify
```

**Rationale.** Feature folders cho component grouping by screen (Inventory components together); primitives folder for reusable atoms; mirror Sprint-3/4/5 backend's module-quartet thinking at frontend scale. TanStack Router file-based routing maps 1:1 to sidebar nav for navigability.

### KTD4 — Authentication header strategy: JWT bearer + explicit X-Tenant-Slug echo

**Decision.** Every API call includes both `Authorization: Bearer <jwt>` (JWT carries `tenant_slug` claim) AND `X-Tenant-Slug: <slug>` (explicit echo). On conflict between the two, backend `TenantRoutingMiddleware` returns 403 + audit row per existing Phase-0-redux D4 priority rules. Frontend computes both from one JWT-decode + sends together.

**Rationale.** Matches INTEGRATION §2 verbatim — JWT claim is the primary; explicit header is the audit surface. Redundancy is intentional: if a buggy client sends mismatched values, the backend catches it before tenant data leaks. Frontend's `httpClient` middleware sets both headers from a single auth-state read.

### KTD5 — MediatR commands/queries for Inventory.Api (existing pattern)

**Decision.** Inventory.Api controllers send MediatR commands/queries; handlers live in `Inventory.Application/Queries/` + `Inventory.Application/Commands/`. Pattern mirrors `AddShopFlowDefaults` registration from Sprint-1-redux + Sprint-4 + Sprint-5.

**Rationale.** Existing convention — wired through `AddShopFlowDefaults`; doesn't require new infrastructure. Sprint-1-redux + Sprint-2-redux + Sprint-4 + Sprint-5 all use it. Bypassing would break consistency.

### KTD6 — Polling implementation: TanStack Query `refetchInterval: 2000`

**Decision.** SKU table list query uses `refetchInterval: 2000` (refetch every 2s while window focused). Drawer ledger query fetches on-open only (no polling — drawer is short-lived; ledger updates flow through SKU table re-fetch which re-renders open drawer if SKU matches). KPI strip polls 2s alongside SKU table.

**Rationale.** TanStack Query's `refetchInterval` integrates with `refetchIntervalInBackground: false` (default) so polling pauses on inactive tab. Sprint-7's SignalR swap replaces the query function body — component code stays unchanged. Drawer no-polling keeps server load proportional to user attention.

### KTD7 — `<ComingSoon>` component + roadmap-aware stub routes

**Decision.** Single `<ComingSoon screen="dashboard" targetSprint={7}>` component renders Lucide icon (matched to screen) + screen name (i18n) + "Coming Sprint-{N}" message + 1-paragraph blurb describing what'll ship. 8 stub route files in `web/src/routes/_coming-soon/`: dashboard, orders, channels, compliance, audit, onboarding, settings, tenants-admin. Each is a 3-line file calling `<ComingSoon>` with appropriate props.

**Rationale.** DRY single source of truth; per-route file count matches sidebar nav (TanStack Router file convention). 30-min cost per STYLING_SPECS estimate.

### KTD8 — Test scope: Vitest + RTL + jest-axe; NO Playwright/visual regression in Sprint-6

**Decision.** Frontend tests: Vitest + React Testing Library for components + hooks + a11y (via `vitest-axe`). Backend tests follow existing Sprint-N pattern (xUnit + Testcontainers). No Playwright E2E, no visual regression, no Storybook.

**Rationale.** Setup overhead for E2E + visual regression exceeds Sprint-6 budget. Vertical slice has 1 screen — unit + integration tests cover behavior. Playwright shines for cross-screen flows; deferred until Sprint-7+ ships 2+ screens. Visual regression valuable but premature at 1 screen.

### KTD9 — Logo SVG + favicon assets generated in U2

**Decision.** Extract dot-matrix mark from prototype's `app.jsx` `<Sidebar>` (~line 50, 4×4 grid of 4px dots) → single SVG sprite at `web/public/logo.svg` with `currentColor` fill. Favicon PNG set generated at 16/32/180/192/512 + SVG favicon. Logo used at 14px (top-bar), 64px (login screen). All monochrome.

**Rationale.** STYLING_SPECS §1 recommends; ~30min effort; ships in U2 token foundation work alongside tokens.css. Channel marketplace logos deferred until legal review.

---

## Output Structure

New directory hierarchy:

```
web/                                       (new — frontend root)
├── public/
│   ├── logo.svg                          U3 — dot-matrix mark extracted from prototype
│   ├── favicon.svg
│   ├── favicon-16.png / favicon-32.png / favicon-180.png / favicon-192.png / favicon-512.png
│   └── fonts/                            U2 — IBM Plex Sans + Plex Mono woff2 (subsetted vi+latin-ext+latin)
│       ├── ibm-plex-sans-400.woff2
│       ├── ibm-plex-sans-500.woff2
│       ├── ibm-plex-sans-600.woff2
│       ├── ibm-plex-sans-700.woff2
│       ├── ibm-plex-mono-400.woff2
│       ├── ibm-plex-mono-500.woff2
│       └── ibm-plex-mono-600.woff2
├── src/
│   ├── routes/                           U6 — TanStack Router file-based
│   │   ├── __root.tsx                   shell (TopBar + Sidebar + Outlet)
│   │   ├── _auth.tsx                    layout requiring auth
│   │   ├── login.tsx                    U5
│   │   ├── _auth/inventory.tsx          U9, U10, U11, U12
│   │   └── _coming-soon/
│   │       ├── dashboard.tsx
│   │       ├── orders.tsx
│   │       ├── channels.tsx
│   │       ├── compliance.tsx
│   │       ├── audit.tsx
│   │       ├── onboarding.tsx
│   │       ├── settings.tsx
│   │       └── tenants-admin.tsx
│   ├── components/
│   │   ├── primitives/                   U3
│   │   │   ├── Button.tsx, Pill.tsx, KPICard.tsx, Drawer.tsx, Modal.tsx, Toast.tsx
│   │   │   ├── Skeleton.tsx, EmptyState.tsx, ComingSoon.tsx
│   │   │   └── Logo.tsx
│   │   ├── shell/                        U3
│   │   │   ├── TopBar.tsx, Sidebar.tsx, TenantPill.tsx, LiveIndicator.tsx
│   │   │   └── LocaleSwitcher.tsx
│   │   └── inventory/                    U9-U12
│   │       ├── SkuTable.tsx, FilterStrip.tsx, KpiStrip.tsx
│   │       ├── LedgerDrawer.tsx, AllocationBar.tsx
│   │       ├── AdjustStockModal.tsx, ThresholdInlineEdit.tsx
│   │       ├── FlashSaleToggle.tsx, CreateSkuModal.tsx
│   │       └── ... (test files alongside)
│   ├── hooks/
│   │   ├── useAuth.ts, useTenant.ts      U5
│   │   ├── useInventoryQuery.ts          U9
│   │   ├── useInventoryMutations.ts      U11, U12
│   │   ├── useLocale.ts                  U3
│   │   └── useIdempotencyKey.ts          U11
│   ├── api/
│   │   ├── httpClient.ts                 U5 — fetch wrapper + auth + idempotency
│   │   ├── auth.ts                       U5
│   │   ├── inventory.ts                  U9-U12
│   │   └── types/                        shared API contract types
│   ├── locales/
│   │   ├── vi-VN.ts                      U3 — port from prototype i18n.jsx
│   │   └── en-US.ts
│   ├── tokens/
│   │   ├── tokens.css                    U2 — copy from prototype + delta patch + a11y fixes
│   │   ├── tokens-settings.css           U2 — copy from prototype
│   │   └── fonts.css                     U2 — @font-face for self-hosted Plex
│   ├── types/                            shared TS types
│   ├── lib/
│   │   ├── format.ts                     fmtVND, fmtAge, fmtLatency — port from prototype data.jsx
│   │   ├── slugify.ts
│   │   └── ulid.ts                       client-side ULID for Idempotency-Key
│   └── main.tsx, App.tsx, index.css
├── tests/                                or alongside src/ — TBD U13
├── index.html                            with <1024px notice block + IBM Plex preconnect
├── package.json
├── pnpm-lock.yaml
├── tsconfig.json
├── vite.config.ts
├── vitest.config.ts
├── .eslintrc.json / eslint.config.js
├── .prettierrc
├── .gitignore                            node_modules/, dist/, .vite/
└── README.md                             frontend-specific quickstart

src/Services/Auth/                         (new — fake auth module quartet, U4)
├── ShopFlow.Auth.Domain/
│   └── (placeholder marker class — real domain in Sprint-7)
├── ShopFlow.Auth.Application/
│   └── (placeholder marker class — real handlers in Sprint-7)
├── ShopFlow.Auth.Infrastructure/
│   └── (placeholder marker class — real JWT signer in Sprint-7)
└── ShopFlow.Auth.Api/
    ├── Controllers/AuthController.cs     fake POST /auth/login
    ├── Program.cs
    ├── appsettings.json
    └── ShopFlow.Auth.Api.csproj

.gitattributes                             (new — U1, CRLF normalisation)
.github/workflows/ci.yml                   (modified — U13, add frontend job)
ShopFlow.sln                               (modified — U1, U4 add 5 csproj)
```

Plus modifications:
- `src/Services/Inventory/ShopFlow.Inventory.Api/Controllers/` — new InventoryController + SkusController (U7, U8)
- `src/Services/Inventory/ShopFlow.Inventory.Application/Queries/` + `Commands/` — MediatR handlers (U7, U8)
- `src/ApiGateway/ShopFlow.Gateway/appsettings.json` — add `auth-api` + `inventory-api` routes (U4, U7)
- `src/AppHost/ShopFlow.AppHost/Program.cs` — register `auth-api` + ensure `inventory-api` registered (U4)
- `docs/phase-gates/2026-05-XX-sprint-6-signoff.md` (new)
- `docs/CHANGELOG.md` (modified)
- `README.md` (modified — current-stage block + badge)
- `CLAUDE.md` (modified — current-stage + sprint history)

---

## Implementation Units

### U1. `web/` Vite scaffold + pnpm + TypeScript + tooling + `.gitattributes`

**Goal:** Bootstrap empty React + TypeScript + Vite project at `web/`. pnpm package manager. Linting + formatting + TS config. Repo-level `.gitattributes` cho CRLF normalisation (closes friction mode 6 from methodology writeup).

**Requirements:** R1, R24.

**Dependencies:** none.

**Files:**
- `web/package.json` (new)
- `web/pnpm-lock.yaml` (new, generated)
- `web/tsconfig.json` (new — strict mode, paths alias `@/*` → `./src/*`)
- `web/tsconfig.node.json` (new — for vite.config.ts)
- `web/vite.config.ts` (new — SWC plugin, port 5173, alias)
- `web/eslint.config.js` (new — typescript-eslint + react + react-hooks plugins)
- `web/.prettierrc` (new — match prototype style: 2-space indent, single quotes, trailing comma)
- `web/.gitignore` (new — `node_modules/`, `dist/`, `.vite/`, coverage)
- `web/index.html` (new — placeholder for U2-U3 work)
- `web/src/main.tsx` (new — minimal `createRoot` + React StrictMode + placeholder App)
- `web/src/App.tsx` (new — placeholder "Sprint-6 scaffold")
- `web/README.md` (new — frontend quickstart: `pnpm install && pnpm dev`)
- `.gitattributes` (new at repo root — `* text=auto`, `*.cs text eol=lf`, `*.ts text eol=lf`, `*.tsx text eol=lf`, `*.json text eol=lf`)
- `ShopFlow.sln` (modified — add solution folder `web/` as solution items for visibility; csproj entries land in U4)

**Approach:**
- `pnpm create vite@latest web --template react-ts` produces the initial scaffold; remove generated demo assets.
- Dependencies declared in `web/package.json`:
  - Runtime: `react@^19`, `react-dom@^19`, `@tanstack/react-router@latest`, `@tanstack/react-query@^5`, `zustand@^5`, `lucide-react@latest` (~0.460 per prototype)
  - Dev: `vite@^6`, `@vitejs/plugin-react-swc`, `typescript@~5.6`, `vitest@^2`, `@testing-library/react@^16`, `@testing-library/jest-dom`, `vitest-axe`, `jsdom`, `@types/react`, `@types/react-dom`, eslint stack, prettier
- TypeScript `strict: true`; `noUncheckedIndexedAccess: true`; path alias `@/*` → `./src/*`.
- Vite config: SWC plugin; React 19 features enabled; dev server proxy `/api/*` → `http://localhost:8080` (Gateway port) so dev mode hits real backend.
- `.gitattributes`: `* text=auto eol=lf` defaults + Windows exceptions for any `.bat`. Closes Sprint-5 friction mode 6 (CRLF noise on every commit) as bonus cleanup.

**Patterns to follow:**
- Existing repo conventions: kebab-case files, sealed/readonly where applicable (frontend analog: `const` + readonly types)
- Sprint-3/4/5 backend module shape — `Domain` / `Application` / `Infrastructure` / `Api` mapping mental model

**Test scenarios:**
- Smoke: `pnpm install && pnpm build` exits 0; produces `dist/` with `index.html` + main bundle.
- Smoke: `pnpm dev` serves `localhost:5173`; default page renders without console errors.
- `tsc --noEmit` exits 0 — type-check passes on scaffold.
- `pnpm lint` exits 0.

**Verification:** `pnpm install + pnpm build + pnpm test + pnpm lint` all green. `git diff` shows only `web/` + `.gitattributes` + `ShopFlow.sln` solution-folder entry. No existing `src/` or `tests/` changed.

---

### U2. Token CSS layer + IBM Plex self-host + STYLING_SPECS delta patch + a11y fixes

**Goal:** Port prototype's `tokens.css` + `tokens-settings.css` to `web/src/tokens/`; apply STYLING_SPECS §3.3 delta patch (30+ lines new tokens) and §6 a11y contrast fixes (`--ink-3` re-point + `<html lang="vi-VN">` + focus-visible spec). Self-host IBM Plex Sans + Plex Mono woff2 files subsetted to `vietnamese + latin-ext + latin`. Update `README.md` + `web/index.html` to remove stale "Inter Tight" / "Inter" references.

**Requirements:** R2, R15, R17.

**Dependencies:** U1.

**Files:**
- `web/src/tokens/tokens.css` (new — copy from prototype + delta patch inline)
- `web/src/tokens/tokens-settings.css` (new — copy from prototype)
- `web/src/tokens/fonts.css` (new — `@font-face` declarations for self-hosted Plex)
- `web/public/fonts/ibm-plex-sans-400.woff2` (new)
- `web/public/fonts/ibm-plex-sans-500.woff2` (new)
- `web/public/fonts/ibm-plex-sans-600.woff2` (new)
- `web/public/fonts/ibm-plex-sans-700.woff2` (new)
- `web/public/fonts/ibm-plex-mono-400.woff2` (new)
- `web/public/fonts/ibm-plex-mono-500.woff2` (new)
- `web/public/fonts/ibm-plex-mono-600.woff2` (new)
- `web/src/index.css` (new — imports tokens + fonts + base reset)
- `web/index.html` (modified — `<html lang="vi-VN">` default, IBM Plex preconnect removed (self-hosted), <1024px notice block bilingual)
- `web/src/main.tsx` (modified — import `./index.css`)

**Approach:**
- Use `glyphhanger` or similar to subset Plex woff2 to vi + latin-ext + latin (cuts ~70% file size per STYLING_SPECS §2.4).
- Source woff2: IBM Plex SIL OFL fonts from https://github.com/IBM/plex — pin a specific release version.
- Paste STYLING_SPECS §3.3 delta block verbatim into `tokens.css` `:root` (--neutral-300, --text-3xl/4xl, --ok-ink, --focus-ring-color, --focus-ring, --duration-fast/medium/slow, --ease-out/in/in-out, --z-* scale).
- A11y fix #1: `--ink-3: var(--neutral-500);` (was hardcoded `#B6B3A6` which fails AA at 3.05:1).
- A11y fix #2: usage sweep — any `var(--primary-500)` on body text <14px → change to `var(--primary-600)` per STYLING_SPECS §6.1.
- A11y fix #3: paste focus-visible CSS block from STYLING_SPECS §3.3 delta.
- `fonts.css`: 7 `@font-face` declarations with `font-display: swap`, `unicode-range` for vi+latin-ext+latin only.
- `README.md` cleanup: remove "Inter Tight" mention; reference IBM Plex.
- `index.html` cleanup: remove Google Fonts preconnect link (self-hosted now).

**Patterns to follow:**
- Prototype files: `tokens.css`, `tokens-settings.css`, `index.html` — direct read + port.
- STYLING_SPECS §3.3 — paste-able CSS block as-is.

**Test scenarios:**
- Visual smoke: open `pnpm dev` localhost:5173; verify Plex Sans + Mono load (network tab shows woff2 200s, not Google Fonts).
- A11y: automated contrast check passes — `--ink-3` on `--bg` reads ≥4.5:1; `--primary-600` on white reads ≥4.5:1.
- Build: `pnpm build` includes woff2 files in `dist/fonts/`.
- Token coverage check: `--neutral-300`, `--text-3xl`, `--text-4xl`, `--ok-ink`, `--focus-ring-color`, `--duration-fast`, `--z-drawer` all defined in `:root` (grep-style assertion).
- README has zero "Inter" references after cleanup.

**Verification:** Visual check matches prototype's amber-ochre + tabular numerals + 1px borders. A11y contrast tool reports zero AA failures on `<body>` + `--ink-3` + `--primary-*` usages.

---

### U3. App shell — TopBar + Sidebar + Logo + Favicon + i18n + ComingSoon primitive

**Goal:** Render the app shell (TopBar with logo + tenant pill + locale switcher + live indicator placeholder; Sidebar with 9 nav items + System Health placeholder section). Extract dot-matrix logo from prototype `app.jsx` into reusable `<Logo size={n}>` SVG component. Generate favicon set. Port i18n.jsx vi-VN + en-US dictionaries to TS files; persist locale to localStorage; update `<html lang>` on switch. Create `<ComingSoon>` primitive for stub routes (used by U6).

**Requirements:** R3 (TopBar shell), R12 (ComingSoon primitive), R14 (locale), R16 (responsive), R18 (Logo SVG), R19 (Favicon).

**Dependencies:** U1, U2.

**Files:**
- `web/src/components/shell/TopBar.tsx` (new + test)
- `web/src/components/shell/Sidebar.tsx` (new + test)
- `web/src/components/shell/TenantPill.tsx` (new + test)
- `web/src/components/shell/LiveIndicator.tsx` (new — placeholder pulse dot; real status in Sprint-7)
- `web/src/components/shell/LocaleSwitcher.tsx` (new + test)
- `web/src/components/primitives/Logo.tsx` (new + test — accepts `size` prop)
- `web/src/components/primitives/ComingSoon.tsx` (new + test — accepts `screen`, `targetSprint`, `icon` props)
- `web/src/components/primitives/Button.tsx` (new — used by TopBar buttons)
- `web/src/components/primitives/Pill.tsx` (new — used by TenantPill)
- `web/public/logo.svg` (new — generated from `app.jsx` dot-matrix)
- `web/public/favicon.svg` (new)
- `web/public/favicon-16.png`, `favicon-32.png`, `favicon-180.png`, `favicon-192.png`, `favicon-512.png` (new — generated via Figma/Squoosh/ImageMagick)
- `web/src/locales/vi-VN.ts` (new — port from prototype `i18n.jsx` VN dictionary)
- `web/src/locales/en-US.ts` (new — port from EN dictionary)
- `web/src/hooks/useLocale.ts` (new + test — Zustand store + localStorage persistence)
- `web/index.html` (modified — `<link rel="icon">` set; `<html>` `lang` attribute managed by JS)
- `web/src/App.tsx` (modified — render shell)

**Approach:**
- **Logo SVG extraction:** read `app.jsx` `<Sidebar>` JSX (~line 50) — 4×4 grid of 4px dots — translate to single `<svg viewBox="0 0 28 28">` with 4 columns × 4 rows of `<rect>` shapes. `currentColor` fill, no stroke. Component: `<Logo size={28} />` scales via CSS `width/height`. Mark via `fill="currentColor"` so dark/active sidebar inverts via CSS.
- **Favicon generation:** use online tool (e.g., realfavicongenerator.net) OR `pnpm dlx @realfavicon/cli` from the SVG; output 16/32/180/192/512 PNG + favicon.svg + site.webmanifest. Drop in `web/public/`.
- **Locale store (Zustand):** `useLocale` exposes `{ lang, setLang }`. Hydrates from `localStorage.shopflow_lang` on init (defaults `vi-VN`). On `setLang`: writes localStorage + sets `document.documentElement.lang`. STYLING_SPECS §6.4 fix.
- **i18n keys:** preserve prototype's key shape (e.g., `inventory.adjust_stock`, `tenant_pill.label`). Translation function `t(key)` reads from `useLocale().dict`. Use `useLocale()` directly in components; no need for i18n library at this scale.
- **TopBar:** logo + wordmark + tenant pill + breach banner placeholder + locale switcher + help button + reviewer-mode toggle + notification bell placeholder + user avatar. Live indicator pulsing dot bound to placeholder state (always "connected" in Sprint-6).
- **Sidebar:** 9 nav items (Inventory active, 8 ComingSoon placeholders). Active state: `--bg-soft` background + `--primary-500` indicator bar. Hover: `--neutral-50`.
- **`<ComingSoon screen={key} targetSprint={N}>`** component: Lucide icon (mapped per screen) + screen name (i18n) + "Coming Sprint-{N}" header + roadmap blurb (i18n key per screen). Centered layout.
- **`<1024px notice`** lives in `index.html` `<noscript>`-style block — hidden at ≥1024px via CSS; shown otherwise per STYLING_SPECS §7 + design note 08.

**Patterns to follow:**
- Prototype: `app.jsx` `<TopBar>` (~line 115), `<Sidebar>` (~line 50). Direct port to TSX with type annotations.
- Prototype: `i18n.jsx` for keys + translation function shape.
- Prototype: `tour.jsx` for `data-tour` attribute placement (preserve in component output).

**Test scenarios:**
- **TopBar renders all sections**: tenant pill, locale switcher, user avatar visible.
- **Logo scales correctly**: `<Logo size={14}>` renders 14×14 SVG; `<Logo size={64}>` renders 64×64. `currentColor` inherits.
- **Sidebar shows 9 nav items**: Inventory + 8 ComingSoon. Active state on Inventory.
- **LocaleSwitcher persists to localStorage**: click switcher → `localStorage.shopflow_lang === 'en-US'`; reload page → still en-US; `document.documentElement.lang === 'en-US'`.
- **`<html lang>` updates dynamically on locale switch**: assert via `document.documentElement.lang` after click.
- **ComingSoon renders for each screen**: render `<ComingSoon screen="dashboard" targetSprint={7}>` → contains "Dashboard" + "Sprint 7" + correct Lucide icon.
- **Vietnamese diacritics render** in TopBar tenant pill (`Yến Sào Khánh Hòa`) — visual + DOM check.
- **`<1024px>` notice hidden ≥1024px**: simulated viewport at 1023px shows notice; 1024px hides.

**Verification:** Visual diff against prototype `index.html` shows pixel-equivalent shell at 1280px width. `useLocale` test suite passes (5+ tests). A11y check: focus-visible ring on TopBar + Sidebar interactive elements.

---

### U4. Backend `ShopFlow.Auth.Api` stub module + Inventory.Api JwtBearer registration

**Goal:** New 4-csproj quartet for `ShopFlow.Auth.{Domain,Application,Infrastructure,Api}`. `Auth.Api` ships fake `POST /auth/login` returning baked JWT signed with dev-only HMAC secret + carrying `tenant_slug` claim. Register `JwtBearer` authentication scheme in `Inventory.Api` (and any other module API that the frontend will call directly through Gateway). Update Gateway `appsettings.json` + AppHost `Program.cs`.

**Requirements:** R3, R22.

**Dependencies:** none (parallel to U1-U3 frontend work).

**Files:**
- `src/Services/Auth/ShopFlow.Auth.Domain/ShopFlow.Auth.Domain.csproj` (new — marker class only)
- `src/Services/Auth/ShopFlow.Auth.Domain/AuthDomainMarker.cs` (new)
- `src/Services/Auth/ShopFlow.Auth.Application/ShopFlow.Auth.Application.csproj` (new — marker class only)
- `src/Services/Auth/ShopFlow.Auth.Application/AuthApplicationMarker.cs` (new)
- `src/Services/Auth/ShopFlow.Auth.Infrastructure/ShopFlow.Auth.Infrastructure.csproj` (new — placeholder for Sprint-7 real signer)
- `src/Services/Auth/ShopFlow.Auth.Infrastructure/AuthInfrastructureMarker.cs` (new)
- `src/Services/Auth/ShopFlow.Auth.Api/ShopFlow.Auth.Api.csproj` (new)
- `src/Services/Auth/ShopFlow.Auth.Api/Program.cs` (new — minimal host + ProblemDetails + controllers)
- `src/Services/Auth/ShopFlow.Auth.Api/Controllers/AuthController.cs` (new — `POST /auth/login` returning baked JWT)
- `src/Services/Auth/ShopFlow.Auth.Api/appsettings.json` (new — dev secret + tenant_slug to bake into JWT)
- `src/Services/Auth/ShopFlow.Auth.Api/appsettings.Development.json` (new)
- `src/Services/Inventory/ShopFlow.Inventory.Api/Program.cs` (modified — register `JwtBearerDefaults.AuthenticationScheme`)
- `src/AppHost/ShopFlow.AppHost/Program.cs` (modified — add `auth-api` resource)
- `src/ApiGateway/ShopFlow.Gateway/appsettings.json` (modified — add `/auth/**` route to auth-api cluster; `/api/v1/inventory/**` route to inventory-api cluster)
- `ShopFlow.sln` (modified — add 4 csproj)
- `tests/ShopFlow.Auth.UnitTests/ShopFlow.Auth.UnitTests.csproj` (new — minimal smoke)
- `tests/ShopFlow.Auth.UnitTests/AuthControllerTests.cs` (new)

**Approach:**
- **AuthController fake login:**
  - Accepts any non-empty `email` + `password` body (POST `/auth/login`).
  - Returns `{ access_token: <JWT>, expires_in: 3600, token_type: "Bearer", user: { email, role: "tenant_seller" } }`.
  - JWT signed with `HS256` using dev-only secret from `appsettings.json` `Auth:DevSecret` (clearly marked DO-NOT-USE-IN-PROD).
  - JWT claims: `sub` = email-as-id, `tenant_slug` = `yensaokhanhhoa` (or config-driven baked tenant), `role` = `tenant_seller`, `exp` = 1 hour, `iss` = `shopflow-dev`, `aud` = `shopflow-api`.
- **JwtBearer in Inventory.Api:**
  - `services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(opts => { opts.TokenValidationParameters = ... })` reading `Auth:DevSecret` + `Auth:Issuer` + `Auth:Audience` from configuration.
  - `[Authorize]` attribute on InventoryController (lands in U7).
  - `TenantRoutingMiddleware` already reads `tenant_slug` claim — no changes needed.
- **Module quartet pattern:** Domain/Application/Infrastructure have only marker classes (e.g., `internal static class AuthDomainMarker { }`). Sprint-7 fills them. Keeping the quartet shape preserves Sprint-7 swap-in.
- **Aspire AppHost:** `var authApi = builder.AddProject<Projects.ShopFlow_Auth_Api>("auth-api");` and `inventoryApi.WithReference(authApi)` if cross-call needed.
- **Gateway routes:** add cluster `auth-cluster` → `http://auth-api:8080`; route `/auth/**` → `auth-cluster`. Mirror existing channel/inventory route pattern.
- **Sign-off note**: AuthController XML doc explicitly tags `<remarks>DEV-MODE STUB — Sprint-7 replaces with real JWT issuance + refresh + denylist. DO NOT DEPLOY.</remarks>`.

**Patterns to follow:**
- Existing module quartet shape: see `src/Services/Channel/ShopFlow.Channel.{Domain,Application,Infrastructure,Api}/` from Sprint-4.
- AppHost project registration: see Sprint-5 stocksync-api addition.
- Gateway appsettings route shape: see existing channel + stocksync entries.

**Test scenarios:**
- **Happy login**: POST `/auth/login` with `{email: "owner@yensao.vn", password: "any"}` → 200 + JWT. Decode JWT → contains `tenant_slug` claim = baked value + `role` = `tenant_seller` + valid `exp`.
- **Empty body**: POST with empty body → 400 + ProblemDetails.
- **JWT signature verification**: signed with secret X; decode with secret X → valid; decode with secret Y → invalid. Standard library behavior; smoke test confirms config wiring.
- **InventoryController without JWT**: GET `/api/v1/inventory/skus` without `Authorization` header → 401. (Test lands in U7 once controller exists.)
- **JWT with wrong tenant_slug**: forge JWT with `tenant_slug` not matching `X-Tenant-Slug` header → backend `TenantRoutingMiddleware` returns 403. (Existing Phase-0-redux D4 behavior; smoke check via integration test.)

**Verification:** `dotnet build` clean (0 errors, 0 warnings). `dotnet test --filter Category!=Integration` passes for new Auth tests. Aspire dev mode shows `auth-api` resource running. Manual smoke: `curl POST http://localhost:8080/auth/login -d '{"email":"owner@yensao.vn","password":"x"}'` returns JWT.

---

### U5. Frontend login screen + JWT storage + auth httpClient

**Goal:** Login screen with amber-ochre tokens — logo + email input + password input + TOTP placeholder stub + submit button. Calls fake `/auth/login`. Stores JWT in localStorage. Creates `httpClient` (fetch wrapper) that injects `Authorization: Bearer <jwt>` + `X-Tenant-Slug: <slug>` (decoded from JWT) on every API call. Idempotency-Key generator hook for writes (lands in U11; placed here for reuse).

**Requirements:** R3, R13.

**Dependencies:** U3 (login screen needs Logo + tokens + Button); U4 (backend `/auth/login` endpoint exists).

**Files:**
- `web/src/routes/login.tsx` (new + test)
- `web/src/components/auth/LoginForm.tsx` (new + test)
- `web/src/hooks/useAuth.ts` (new + test — Zustand store + localStorage persistence)
- `web/src/api/httpClient.ts` (new + test — fetch wrapper)
- `web/src/api/auth.ts` (new — login endpoint typed)
- `web/src/lib/ulid.ts` (new — client-side ULID generator for Idempotency-Key; used in U11)
- `web/src/hooks/useIdempotencyKey.ts` (new — returns new ULID per render OR per submission)

**Approach:**
- **useAuth store (Zustand):** `{ jwt: string|null, user: { email, role, tenantSlug } | null, login: (jwt) => void, logout: () => void }`. On init: read `localStorage.shopflow_auth_jwt` → if present, decode + populate. On `login(jwt)`: persist + populate. On `logout`: clear localStorage + state + redirect to `/login`.
- **JWT decode:** lightweight base64-url decode of payload section. No verification (server verifies; client decodes for UI only). Library: hand-rolled 5-line decode OR `jwt-decode` package (~1KB). Prefer hand-rolled for minimal dep surface.
- **httpClient:** wraps fetch. Inputs: `path`, `init`. On every call: if auth state has JWT, add `Authorization` + `X-Tenant-Slug` headers. For mutations (POST/PUT/DELETE/PATCH): generate ULID via `lib/ulid.ts` + add `Idempotency-Key` header. On 401: clear auth + redirect to login. On other errors: throw typed `ApiError`. Response: JSON-parse + return typed.
- **LoginForm:** email input + password input + submit. Submit button disabled until both non-empty. On submit: call `api/auth.login()`. On success: `useAuth.login(jwt)` + navigate to `/inventory` (TanStack Router `navigate`). On 4xx/5xx: show error toast with idempotency key (per STYLING_SPECS §7 error-state).
- **TOTP placeholder:** UI shows label "Mã 2FA" + 6-digit input + helper text "Bỏ qua trong chế độ phát triển" (skip in dev mode). Disabled in Sprint-6; Sprint-7 wires real verification.
- **Layout:** centered card on `--bg-soft` background, 400px wide, Logo at 64px above form, amber-ochre primary button. Reuse prototype's `<EmptyState>` typography hierarchy.

**Patterns to follow:**
- Prototype: no login screen exists (cut per INTEGRATION §2). Design from STYLING_SPECS §1 + §6 (focus-visible + a11y on form).
- TanStack Router: file-based route at `web/src/routes/login.tsx`; export `Route = createFileRoute('/login')({ component: LoginScreen })`.
- Zustand store pattern: simple `create<State>((set) => ({ ... }))`.

**Test scenarios:**
- **Covers AE1.** Login form renders Logo + email + password + TOTP placeholder + submit button.
- **Empty email → button disabled.** Empty password → button disabled.
- **Submit calls fake /auth/login**: mocked fetch + assert call signature + body. Success → JWT stored in localStorage + navigate to `/inventory`.
- **JWT decode**: useAuth correctly extracts `tenantSlug` + `role` from baked JWT.
- **localStorage persistence**: reload page after login → useAuth populates from localStorage.
- **httpClient adds headers**: GET request → `Authorization: Bearer <jwt>` + `X-Tenant-Slug: yensaokhanhhoa` present.
- **httpClient adds Idempotency-Key for POST/PUT**: each call has unique ULID header.
- **httpClient 401 → logout + redirect**: 401 response triggers `useAuth.logout` + navigate to `/login`.
- **Error state on login failure**: 401 → error toast appears with retry button.
- **A11y**: focus-visible ring on email/password inputs (auto by tokens.css from U2).

**Verification:** End-to-end smoke: open `localhost:5173` → see login screen → submit any creds → backend returns JWT → frontend stores → navigates to `/inventory`. Per AE1.

---

### U6. TanStack Router setup + 8 ComingSoon stub routes + auth-gated layout

**Goal:** Wire TanStack Router with file-based routing. Configure auth-gated layout (`_auth.tsx`) that redirects to `/login` if not authenticated. Create 8 stub routes under `_coming-soon/` each rendering `<ComingSoon>` with appropriate props.

**Requirements:** R12 (stub routes), R13 (auth-gated routing).

**Dependencies:** U3 (`<ComingSoon>` primitive), U5 (`useAuth` exists), U1 (TanStack Router installed).

**Files:**
- `web/src/routes/__root.tsx` (new — root layout, render TopBar + Sidebar + `<Outlet />`)
- `web/src/routes/_auth.tsx` (new + test — auth guard layout; redirect to `/login` if !isAuthenticated)
- `web/src/routes/_auth/_coming-soon/dashboard.tsx` (new)
- `web/src/routes/_auth/_coming-soon/orders.tsx` (new)
- `web/src/routes/_auth/_coming-soon/channels.tsx` (new)
- `web/src/routes/_auth/_coming-soon/compliance.tsx` (new)
- `web/src/routes/_auth/_coming-soon/audit.tsx` (new)
- `web/src/routes/_auth/_coming-soon/onboarding.tsx` (new)
- `web/src/routes/_auth/_coming-soon/settings.tsx` (new)
- `web/src/routes/_auth/_coming-soon/tenants-admin.tsx` (new)
- `web/src/routes/_auth/index.tsx` (new — default landing redirects to `/inventory`)
- `web/src/routeTree.gen.ts` (auto-generated by TanStack Router)
- `web/src/main.tsx` (modified — wire `<RouterProvider>`)
- `web/src/router.ts` (new — `createRouter` instance with auth context)
- `web/src/components/primitives/Sidebar.tsx` (modified from U3 — use `<Link>` from TanStack Router for nav items)

**Approach:**
- **TanStack Router file-based routing:** `_auth` segment = layout requiring auth; nested routes inherit. `_auth/_coming-soon/` is a virtual layout (underscore prefix) so URL is `/dashboard` not `/_coming-soon/dashboard`.
- **`__root.tsx`:** renders shell (TopBar + Sidebar + main `<Outlet />`). Sidebar `<Link>` items navigate to `/dashboard`, `/inventory`, etc.
- **`_auth.tsx` guard:** uses `beforeLoad` hook → if !`useAuth.getState().jwt`, throw redirect to `/login`. TanStack Router native pattern.
- **Each stub route:** 3-line file: `export const Route = createFileRoute('/_auth/_coming-soon/dashboard')({ component: () => <ComingSoon screen="dashboard" targetSprint={7} icon={LayoutDashboard} /> })`.
- **`/inventory` route file lands in U9** (not here; only stubs in this unit).
- **Router context:** pass `{ auth }` so `_auth.tsx` reads auth state without coupling to Zustand directly. Sprint-7 swap-in becomes cleaner.

**Patterns to follow:**
- TanStack Router docs: file-based routing, `createFileRoute`, `beforeLoad` redirects.
- Existing `<Sidebar>` from U3: nav item shape with `href` + `icon` + `label`.

**Test scenarios:**
- **Covers AE5.** Click Dashboard nav → URL `/dashboard` → `<ComingSoon screen="dashboard" targetSprint={7}>` renders.
- **All 8 stub routes navigate**: programmatic test iterating routes + asserting render.
- **Unauthenticated `/inventory` access → redirects to `/login`**: render `_auth` guard with empty auth state.
- **After login, redirected to `/inventory`**: post-login, accessing `/_auth/index` redirects to `/_auth/inventory`.
- **Sidebar nav highlights active route**: visiting `/inventory` shows Inventory active state; `/dashboard` shows Dashboard active state.
- **A11y**: keyboard Tab through Sidebar nav items + Enter activates link.

**Verification:** Manual: log in → all 9 nav items navigate; 8 show ComingSoon; only Inventory shows actual content (placeholder until U9). Direct URL `/inventory` while logged out redirects to login.

---

### U7. Inventory.Api READ controllers — `/skus` list + `/skus/{sku}/ledger` + `/summary` (new Backend Gap)

**Goal:** Inventory.Api ships HTTP read endpoints surfacing existing repository methods. New aggregate endpoint `GET /api/v1/inventory/summary` for KPI strip (Backend Gap closure). Controllers use MediatR queries; handlers in `Inventory.Application/Queries/`.

**Requirements:** R4 (SKU table data), R5 (KPI strip + summary endpoint), R6 (ledger drawer data), R20 (Inventory.Api controllers), R21 (new summary endpoint).

**Dependencies:** U4 (JwtBearer registered).

**Files:**
- `src/Services/Inventory/ShopFlow.Inventory.Api/Controllers/SkusController.cs` (new)
- `src/Services/Inventory/ShopFlow.Inventory.Api/Controllers/InventoryController.cs` (new — summary endpoint)
- `src/Services/Inventory/ShopFlow.Inventory.Application/Queries/ListSkusQuery.cs` (new)
- `src/Services/Inventory/ShopFlow.Inventory.Application/Queries/ListSkusQueryHandler.cs` (new)
- `src/Services/Inventory/ShopFlow.Inventory.Application/Queries/GetSkuLedgerQuery.cs` (new)
- `src/Services/Inventory/ShopFlow.Inventory.Application/Queries/GetSkuLedgerQueryHandler.cs` (new)
- `src/Services/Inventory/ShopFlow.Inventory.Application/Queries/GetInventorySummaryQuery.cs` (new)
- `src/Services/Inventory/ShopFlow.Inventory.Application/Queries/GetInventorySummaryQueryHandler.cs` (new)
- `src/Services/Inventory/ShopFlow.Inventory.Application/Dtos/SkuListDto.cs` (new — read shape per INTEGRATION §1.1)
- `src/Services/Inventory/ShopFlow.Inventory.Application/Dtos/SkuLedgerEntryDto.cs` (new)
- `src/Services/Inventory/ShopFlow.Inventory.Application/Dtos/InventorySummaryDto.cs` (new)
- `tests/ShopFlow.Inventory.IntegrationTests/SkusControllerReadTests.cs` (new — `Category=Integration`)
- `tests/ShopFlow.Inventory.UnitTests/Queries/GetInventorySummaryQueryHandlerTests.cs` (new — `Category=Unit`)

**Approach:**
- **`GET /api/v1/inventory/skus`** params: `filter[cat]`, `filter[channel]`, `filter[state]`, `filter[zone]`, `q`, `page`, `pageSize`. Handler joins `stock_items` + `stock_item_bins` + `reservations_ledger` aggregate per SKU + product mapping (for channel allocation chips). Returns paginated `{ items: SkuListDto[], page, total }`. p24 (24-hour outbound) joined from outbound module's orders — server-side join via raw SQL or EF projection.
- **`GET /api/v1/inventory/skus/{sku}/ledger`** params: `channel`, `cursor`, `pageSize`. Handler reads `reservations_ledger` table filtered by SKU; computes `running_balance` server-side (cumulative sum); orders by `ts DESC`. Returns `{ items: SkuLedgerEntryDto[], allocPerChannel: {...}, nextCursor }`.
- **`GET /api/v1/inventory/summary`** (Backend Gap closure): aggregate query — `total = SUM(on_hand)`, `reserved = SUM(reserved)`, `below_threshold = COUNT(sku WHERE available < threshold)`, `oversell_risk = COUNT(sku WHERE reserved > on_hand)`. Returns `InventorySummaryDto`.
- **MediatR pattern:** controllers inject `IMediator`; each endpoint sends query; handler reads via existing `InventoryDbContext`. Tenant routing via existing middleware (JWT claim).
- **DTOs shape:** match INTEGRATION §1.1 wire shape exactly — INTEGRATION is the canonical contract.
- **Caching:** none in Sprint-6 (premature); summary endpoint may be hit every 2s by polling — handler does fast aggregate query; index hints if needed land in plan-time deferred.

**Patterns to follow:**
- Existing `AddShopFlowDefaults` MediatR registration — handlers auto-discovered via assembly scan.
- Sprint-4 `WebhooksController` shape: thin controller + send command/query.
- Sprint-5 `StockSync.Api` `SkuFlagsController`: idempotent PUT controller pattern (referenced in U8).
- Existing `InventoryDbContext` + entity configs (Sprint-1-redux + Sprint-2-redux).

**Test scenarios:**
- **Covers AE1, R4.** GET `/api/v1/inventory/skus` with valid JWT → 200 + paginated SKU list.
- **Covers R4.** Filter by category → only matching SKUs returned.
- **Covers R4.** Filter by search query `q=YS` → SKUs with name/sku matching returned (case-insensitive).
- **Covers R4.** Pagination: `?page=2&pageSize=50` → 50 SKUs starting at offset 50.
- **Covers AE2, R6.** GET `/api/v1/inventory/skus/YS-TINH-CHE-100G/ledger` → 200 + ledger entries DESC by `ts` + cumulative `running_balance`.
- **Covers R5, R21, AE6.** GET `/api/v1/inventory/summary` → 200 + `{ total, reserved, below_threshold, oversell_risk }` matching seeded data.
- **Unauthorized.** GET `/skus` without `Authorization` header → 401.
- **Wrong tenant.** JWT with `tenant_slug` not matching `X-Tenant-Slug` → 403 + audit row.
- **Tenant isolation**: query as tenant A → returns only A's SKUs; query as tenant B → returns only B's SKUs. (Integration test against 2 provisioned tenant DBs.)
- **Empty result**: filter with no matches → 200 + `{ items: [], total: 0 }`. No error.
- **Cursor pagination ledger**: 1000-entry ledger → 10 pages of 100. `nextCursor` correctly carries forward.

**Execution note:** Test-first cadence for `InventorySummaryQueryHandler` — the SQL aggregate is the load-bearing logic; write failing test for `below_threshold` count first, then handler.

**Verification:** Integration tests pass against Testcontainers Postgres. Manual: `curl http://localhost:8080/api/v1/inventory/skus -H "Authorization: Bearer <jwt>" -H "X-Tenant-Slug: yensaokhanhhoa"` returns paginated SKUs. Tenant isolation holds across 2 tenants.

---

### U8. Inventory.Api WRITE controllers — POST `/adjustments` + PUT `/skus/{sku}/threshold` + POST `/skus`

**Goal:** 3 write endpoints with `Idempotency-Key` header support, MediatR commands, audit row emission, downstream `StockLevelChangedV1` event flow (Sprint-5 U2 path) for adjustments.

**Requirements:** R8 (adjust stock), R9 (set threshold), R11 (create SKU), R20.

**Dependencies:** U7 (DTOs + handler patterns established).

**Files:**
- `src/Services/Inventory/ShopFlow.Inventory.Api/Controllers/SkusController.cs` (modified — add POST `/skus`, PUT `/skus/{sku}/threshold`)
- `src/Services/Inventory/ShopFlow.Inventory.Api/Controllers/AdjustmentsController.cs` (new — POST `/adjustments`)
- `src/Services/Inventory/ShopFlow.Inventory.Application/Commands/AdjustStockCommand.cs` (new)
- `src/Services/Inventory/ShopFlow.Inventory.Application/Commands/AdjustStockCommandHandler.cs` (new)
- `src/Services/Inventory/ShopFlow.Inventory.Application/Commands/SetThresholdCommand.cs` (new)
- `src/Services/Inventory/ShopFlow.Inventory.Application/Commands/SetThresholdCommandHandler.cs` (new)
- `src/Services/Inventory/ShopFlow.Inventory.Application/Commands/CreateSkuCommand.cs` (new)
- `src/Services/Inventory/ShopFlow.Inventory.Application/Commands/CreateSkuCommandHandler.cs` (new)
- `src/Services/Inventory/ShopFlow.Inventory.Application/Idempotency/IdempotencyService.cs` (new — checks/inserts idempotency rows; lookup keyed by `(tenant_id, idempotency_key)`)
- `src/Services/Inventory/ShopFlow.Inventory.Infrastructure/Migrations/20260518000001_AddIdempotencyTable.cs` (new — `inventory_idempotency_records` table)
- `tests/ShopFlow.Inventory.IntegrationTests/AdjustStockControllerTests.cs` (new — `Category=Integration`)
- `tests/ShopFlow.Inventory.IntegrationTests/SetThresholdControllerTests.cs` (new)
- `tests/ShopFlow.Inventory.IntegrationTests/CreateSkuControllerTests.cs` (new)

**Approach:**
- **Idempotency table:** new `inventory_idempotency_records` table per tenant DB: `(idempotency_key text PK, request_hash text, response_body jsonb, status_code int, created_at timestamptz)`. Handler checks key on entry: if present + same request hash → return cached response; if present + different hash → 409 conflict; else proceed + cache response.
- **POST `/adjustments`** body: `{ sku, delta, reason, note? }`. Handler validates SKU exists; calls existing `StockItemRepository.AdjustAtBinAsync` (Sprint-2-redux U5); writes `stock_adjustments` row; commits transaction. Downstream `StockLevelChangedV1` event emits per Sprint-5 U2 outbox path (no changes needed — already wired).
- **PUT `/skus/{sku}/threshold`** body: `{ threshold: int }`. Handler updates `stock_items.threshold` directly via EF. Single row update.
- **POST `/skus`** body: `{ sku, name, cat, threshold, initial_total, zone, price, cost, alloc }`. Handler creates `stock_items` row + auto-creates `stock_item_bins` row (initial_total at default zone) per Sprint-2-redux U5 pattern. `product_mappings` rows created lazily on first channel sync (not in this handler).
- **Idempotency key header validation:** controller method attribute `[RequireIdempotencyKey]` (new attribute) checks `Idempotency-Key` header present + valid ULID format; 400 if missing.
- **Audit:** rely on existing `OutboxInterceptor` to capture domain events; each handler raises `StockAdjustedDomainEvent` / `ThresholdChangedDomainEvent` / `SkuCreatedDomainEvent` (new domain events if not yet existing).

**Patterns to follow:**
- Sprint-1-redux U3 `ReservationExpiryWorker` + Sprint-2-redux U5 `AdjustAtBinAsync`: existing repository methods.
- Sprint-4 outbox-write pattern: domain event → `OutboxInterceptor` captures.
- Sprint-5 U2 `StockLevelChangedV1` outbox: emits via existing `IInventoryOutbox` port — no new wiring.

**Test scenarios:**
- **Covers AE3, R8.** POST `/adjustments` `{sku:"X", delta:+10, reason:"recount"}` with valid `Idempotency-Key` header → 200 + adjustment recorded + `stock_items.on_hand` increased by 10 + outbox row for `StockLevelChangedV1` written.
- **R8 idempotency.** POST `/adjustments` twice with same `Idempotency-Key` → second call returns cached response, NO second adjustment row, NO second outbox row.
- **R8 idempotency-conflict.** POST `/adjustments` twice with same key but different body → 409 conflict.
- **Missing `Idempotency-Key` header** → 400 + `{ error: "idempotency_key_required" }`.
- **Invalid ULID format** → 400.
- **Invalid SKU** (doesn't exist) → 404.
- **R9 PUT threshold.** PUT `/skus/X/threshold` `{threshold: 50}` → 200 + `stock_items.threshold = 50`.
- **R9 idempotency.** PUT twice with same key → second returns cached; no double update.
- **R11 POST /skus.** POST `/skus` valid body → 201 + new `stock_items` row + auto-created `stock_item_bins` at default zone + `Location` header.
- **R11 duplicate SKU.** POST `/skus` with existing SKU → 409.
- **Tenant isolation.** Adjustment on tenant A's SKU does not affect tenant B's identically-named SKU. (Integration test against 2 tenants.)

**Execution note:** Test-first for idempotency behavior — the cached-response path is subtle; write the "same key + same body → cached response" test first, then `IdempotencyService`.

**Verification:** All integration tests pass. Manual: 3-call cURL flow — adjust → fetch SKU → see new on_hand value. Replay same `Idempotency-Key` → adjustment count unchanged.

---

### U9. Inventory SKU table screen + filter strip + KPI strip + 2s polling

**Goal:** Frontend Inventory screen rendering SKU table (all columns per prototype + filter pills + KPI strip with summary endpoint data). TanStack Query for fetching with `refetchInterval: 2000`. Per-row actions trigger drawer (lands in U10).

**Requirements:** R4 (SKU table), R5 (KPI strip), R7 (polling).

**Dependencies:** U3 (shell + tokens), U6 (routing + auth guard), U7 (backend `/skus` + `/summary` endpoints).

**Files:**
- `web/src/routes/_auth/inventory.tsx` (new — route file)
- `web/src/components/inventory/SkuTable.tsx` (new + test)
- `web/src/components/inventory/FilterStrip.tsx` (new + test)
- `web/src/components/inventory/KpiStrip.tsx` (new + test)
- `web/src/components/inventory/ChannelAllocationChip.tsx` (new — chip per channel in row)
- `web/src/components/inventory/StatusPill.tsx` (new — `OK / Below threshold / Oversell risk`)
- `web/src/hooks/useInventoryQuery.ts` (new + test — `useQuery` wrapping `api/inventory.listSkus`)
- `web/src/hooks/useInventorySummaryQuery.ts` (new + test — `useQuery` wrapping `api/inventory.summary`)
- `web/src/api/inventory.ts` (new — typed endpoints; consumed across U9-U12)
- `web/src/lib/format.ts` (new + test — `fmtVND`, `fmtAge`, `fmtNum`, `fmtLatency`; port from prototype `data.jsx`)

**Approach:**
- **TanStack Query setup:** Provider in `__root.tsx` (U6 modified) with `queryClient` configured for `refetchInterval: 2000` on inventory queries + `refetchIntervalInBackground: false` (pause on inactive tab).
- **`useInventoryQuery`:** wraps `api/inventory.listSkus({ filter, page, pageSize })`. Returns `{ data, isLoading, error }`. Query key includes filter state so filter changes trigger refetch immediately. Polling auto-runs every 2s.
- **`useInventorySummaryQuery`:** wraps `api/inventory.summary()`. Same polling cadence. Smaller payload.
- **`<SkuTable>`:** column shape per INTEGRATION §1.1 — SKU, name, cat, on_hand, reserved, available, threshold, allocation chips (Shopee/Lazada/TikTok/Shopify chips), status pill, zone, last_updated (relative time). Sticky header. Click row → opens drawer (U10).
- **`<FilterStrip>`:** category multi-select + channel select + state select + zone select + search input. Each filter is server query-string param. Filter state in Zustand or `useState` + URL search params via TanStack Router's `useSearch`.
- **`<KpiStrip>`:** 4 KPI cards — Tồn thực (total) / Đã giữ chỗ (reserved) / Dưới mức an toàn (below_threshold) / Nguy cơ bán vượt (oversell_risk). Each card: label + value (tabular-numeral). Stale-state per STYLING_SPECS §7: prev value at 50% opacity during loading.
- **Empty state:** filter empty → `<EmptyState kind="filter">` with "Xoá tất cả bộ lọc" CTA. True zero (no SKUs) → `<EmptyState kind="zero">` with "Thêm SKU đầu tiên" CTA (opens Create SKU modal in U12).
- **Vietnamese content:** SKU names, customer names, etc. all from real backend (Yến Sào Khánh Hòa fixtures or seeded data). Plex Sans renders diacritics correctly (verified U2).

**Patterns to follow:**
- Prototype: `screen-inventory.jsx` `<DesktopInventory>` (~line 60-200). Direct port; preserve `data-review="ochre"`, `data-review="vn-content"`, `data-review="empty"`, `data-review="border-card"` attributes per STYLING_SPECS §6.5.
- TanStack Query v5: `useQuery` with `refetchInterval` option.

**Test scenarios:**
- **Covers AE1.** Login → navigate to `/inventory` → SKU table renders with seeded data (≥1 SKU visible).
- **Covers R7.** Open Inventory → wait 2s → background re-fetch fires (mock fetch + assert called 2x within 3s).
- **Covers AE6, R5, R21.** KPI strip renders 4 cards with values from `/summary` endpoint. Numbers update on each poll.
- **Filter category** → table filters to matching SKUs.
- **Filter search "YS"** → table filters to SKUs matching "YS" pattern.
- **Empty filter result** → shows `<EmptyState kind="filter">` with "Xoá tất cả bộ lọc" button.
- **Clear filters** → restores full list.
- **Vietnamese SKU names render** (no diacritic clipping; checks DOM text).
- **A11y**: focus-visible ring on filter inputs + search; table rows keyboard-navigable via Tab.
- **Loading state**: initial mount shows skeleton rows (per STYLING_SPECS §7); replaced with data when fetch resolves.
- **Stale state**: SignalR disconnect simulation → KPI values stay; live indicator (placeholder) shows warning state.
- **Tabular numerals**: all numeric columns use `font-variant-numeric: tabular-nums` (assert computed style).

**Verification:** Manual: log in → see Inventory table populated; KPI strip shows real numbers; filters work; click row queues drawer (drawer body lands U10). Visual diff against prototype `screen-inventory.jsx`: column shape + spacing pixel-equivalent at 1280px.

---

### U10. Reservation Ledger drawer with `running_balance` + allocation bar

**Goal:** Drawer component opens on SKU row click; fetches ledger from `/skus/{sku}/ledger`; renders append-only entries with `running_balance` column + per-channel allocation bar at top. Drawer slides in 150ms per STYLING_SPECS §4. Esc / click-outside / X closes.

**Requirements:** R6 (drawer with ledger), R7 (drawer fetches on open, no polling).

**Dependencies:** U9 (SKU table emits row-click event; click target propagates to drawer).

**Files:**
- `web/src/components/inventory/LedgerDrawer.tsx` (new + test)
- `web/src/components/inventory/AllocationBar.tsx` (new + test)
- `web/src/components/inventory/LedgerRow.tsx` (new + test)
- `web/src/components/primitives/Drawer.tsx` (new + test — reusable drawer primitive for Sprint-7+ reuse)
- `web/src/hooks/useLedgerQuery.ts` (new + test — `useQuery` wrapping `api/inventory.getLedger`)
- `web/src/api/inventory.ts` (modified — add `getLedger` endpoint)

**Approach:**
- **`<Drawer>` primitive:** reusable. Props: `isOpen`, `onClose`, `title`, `width` (default 620px per STYLING_SPECS §7), `children`. Slides in from right via CSS `transform: translateX`; backdrop mask with `backdrop-filter: blur(2px)`. Esc keyboard handler + click-outside-mask handler call `onClose`. ARIA: `role="dialog"`, `aria-modal="true"`, `aria-labelledby` per STYLING_SPECS §6.4.
- **`<LedgerDrawer>` composition:** renders inside `<Drawer>`. Top section: `<AllocationBar>` showing per-channel allocation (Shopee 40% / Lazada 30% / TikTok 20% / Shopify 10% as filled bar). Body: `<table>` with columns ts / channel / qty / kind (reserve/release/confirm/adjust) / status / ref_order (clickable to Order screen — Sprint-7) / running_balance. Each ledger entry is a `<LedgerRow>`.
- **`useLedgerQuery`:** wraps `api/inventory.getLedger({ sku, cursor })`. Fetches on drawer open (when `sku` prop changes from null to a value); does NOT poll. Cursor-based pagination via "Tải thêm" button at bottom (load more).
- **`<AllocationBar>`:** horizontal stacked bar with per-channel color segments. Channel colors from token CSS (`--ch-shopee`, `--ch-lazada`, etc. per STYLING_SPECS §3.1).
- **Drawer integration:** `<SkuTable>` from U9 manages `selectedSku` state. Click row → set state. `<LedgerDrawer sku={selectedSku} isOpen={selectedSku !== null} onClose={() => setSelectedSku(null)}>`.
- **Empty state:** zero ledger entries → "Chưa có giao dịch nào" message.
- **Live update sync:** when SKU table polls + fetches new data, if drawer is open for that SKU → drawer's ledger query re-fetches (TanStack Query invalidates by query key match). Sprint-7 SignalR replaces this with push.

**Patterns to follow:**
- Prototype: `screen-inventory.jsx` `<LedgerDrawer>` (~line 210-310). Direct port. Preserve `data-review="saga"` attribute placement.
- STYLING_SPECS §4 motion specs: drawer slide-in 150-250ms.

**Test scenarios:**
- **Covers AE2.** Click SKU row → drawer slides in (150ms via transition assertion). Ledger fetches + renders entries DESC by `ts`. `running_balance` column shows cumulative values.
- **Drawer ARIA:** role="dialog", aria-modal="true". Focus traps inside drawer when open.
- **Esc closes drawer**: keyboard event → drawer state `isOpen: false`.
- **Click outside (mask)** → drawer closes.
- **Click X button** → drawer closes.
- **AllocationBar renders 4 channels** with widths matching backend `allocPerChannel` percentages.
- **Empty ledger** → "Chưa có giao dịch nào" message instead of empty table.
- **Vietnamese channel names render** correctly (`Shopee`, `Lazada`, `TikTok Shop`, `Shopify`).
- **A11y**: focus-visible inside drawer; tab order logical (close button last).
- **No drawer polling**: assert `useQuery` config has no `refetchInterval` (or refetchInterval: false).

**Verification:** Manual end-to-end: log in → Inventory → click first SKU row → drawer opens with ledger entries + allocation bar. Esc closes. Visual diff against prototype `<LedgerDrawer>` at 620px width.

---

### U11. Adjust stock modal + Set threshold inline edit (Owner writes)

**Goal:** Two write paths from Owner. (1) "Điều chỉnh tồn" button in drawer opens modal with delta + reason + optional note → POST `/adjustments` with auto-generated `Idempotency-Key`. (2) Threshold column inline edit → PUT `/skus/{sku}/threshold`. Both show toast confirmation; both auto-revert on failure.

**Requirements:** R8 (adjust stock), R9 (set threshold).

**Dependencies:** U10 (drawer where Adjust button lives), U8 (backend POST `/adjustments` + PUT threshold endpoints), U5 (idempotency key generator).

**Files:**
- `web/src/components/inventory/AdjustStockModal.tsx` (new + test)
- `web/src/components/inventory/ThresholdInlineEdit.tsx` (new + test)
- `web/src/components/primitives/Modal.tsx` (new + test — reusable modal primitive)
- `web/src/components/primitives/Toast.tsx` (new + test — reusable toast primitive)
- `web/src/hooks/useInventoryMutations.ts` (new + test — `useMutation` for adjustStock + setThreshold)
- `web/src/hooks/useToast.ts` (new + test — Zustand store for toast queue)

**Approach:**
- **`<Modal>` primitive:** similar to Drawer but centered, smaller (480px). Reuses focus-trap logic. Backdrop blur + click-outside-to-close.
- **`<AdjustStockModal>`:** form with: numeric delta input (positive or negative), reason dropdown (recount / damage / theft / found / other), optional note textarea, submit + cancel buttons. Submit button disabled if delta=0 or no reason.
- **`useInventoryMutations`:**
  - `adjustStock`: `useMutation({ mutationFn: api.adjustStock, onSuccess, onError })`. On success: invalidate `inventory-skus` + `inventory-summary` + `inventory-ledger` query keys (triggers re-fetch). Show success toast.
  - `setThreshold`: same pattern.
  - Idempotency-Key generated via `useIdempotencyKey` hook (U5) — ULID per submission, regenerated on retry.
- **`<ThresholdInlineEdit>`:** appears on hover of threshold cell. Click → input field with current value. Enter or blur → PUT request. Esc → revert. Optimistic UI: input shows new value immediately; on error, revert + error toast.
- **`<Toast>` primitive:** queue managed by Zustand `useToast` store. Renders bottom-right per STYLING_SPECS §7 (toast dwell 8s for errors, 4s for success). Toast shows idempotency key + trace ID on error per STYLING_SPECS §7.
- **Error handling:** on 4xx/5xx → `useMutation.onError` → toast with `<idempotency_key + trace_id>` per STYLING_SPECS error-state. User can retry; new ULID generated.
- **Audit demo:** after adjust → re-poll triggers within 2s → ledger drawer (if open) shows new entry; SKU table row shows new `on_hand` value. This is THE methodology demo: end-to-end write + audit visibility.

**Patterns to follow:**
- Prototype: `screen-inventory.jsx` `<DesktopInventory>` bulk actions (~line 122) — adapt to single-SKU adjust.
- STYLING_SPECS §7 toast specs.
- TanStack Query `useMutation` + query invalidation.

**Test scenarios:**
- **Covers AE3.** Click "Điều chỉnh tồn" in drawer → modal opens. Submit delta=+10 reason="recount" → POST `/adjustments` fired with `Idempotency-Key` header. Backend returns 200. Success toast. Within 2s, SKU table row shows on_hand+10; drawer ledger shows new entry.
- **Adjust delta=0** → submit button disabled.
- **Adjust without reason** → submit disabled.
- **Adjust with note** → note included in request body.
- **Adjust failure** (mock 500) → error toast with idempotency_key + trace_id; modal stays open; user can retry.
- **Retry adjust** → new ULID generated (idempotency_key differs from first attempt).
- **R9 inline threshold edit**: hover threshold cell → edit affordance visible → click → input appears. Type new value + Enter → PUT request → success toast. Cell shows new value.
- **Threshold Esc → reverts** to original value without save.
- **Threshold failure** → revert + error toast.
- **A11y modal**: focus trap, Esc closes.
- **A11y inline edit**: keyboard reachable; aria-label on input.

**Execution note:** Test-first for `useInventoryMutations` adjustStock — idempotency key generation + query invalidation are subtle. Write failing test for "after success, queries invalidate + refetch" first.

**Verification:** End-to-end manual: open drawer → Adjust → submit → see updated values within 2s. Replay with same idempotency-key (manually via DevTools fetch) → no double-adjustment.

---

### U12. Flash-sale toggle + Create SKU modal (Owner writes)

**Goal:** (1) Flash-sale toggle in drawer header — flips `is_flash_sale` flag via Sprint-5's existing `PUT /api/v1/skus/{sku}/flag` endpoint. Optimistic UI. (2) "SKU mới" button on Inventory page opens Create SKU modal — full form (sku / name / cat / threshold / initial_total / zone / price / cost / alloc) → POST `/skus`.

**Requirements:** R10 (flash-sale toggle), R11 (create SKU).

**Dependencies:** U10 (drawer), U8 (backend POST `/skus` endpoint), U11 (modal + toast primitives).

**Files:**
- `web/src/components/inventory/FlashSaleToggle.tsx` (new + test)
- `web/src/components/inventory/CreateSkuModal.tsx` (new + test)
- `web/src/components/primitives/Toggle.tsx` (new + test — reusable toggle primitive)
- `web/src/hooks/useInventoryMutations.ts` (modified — add `setFlashSale` + `createSku`)
- `web/src/api/inventory.ts` (modified — add `setFlashSale` + `createSku` endpoints)
- `web/src/api/stocksync.ts` (new — Sprint-5 StockSync.Api endpoint — `PUT /api/v1/skus/{sku}/flag`)

**Approach:**
- **`<FlashSaleToggle>`:** placed in drawer header. Renders Toggle primitive + label "Flash-sale routing". Click → optimistic flip → PUT `/api/v1/skus/{sku}/flag`. On success: keep state. On failure: revert + error toast.
- **`<CreateSkuModal>`:** form with all fields per INTEGRATION §1.2. Validation:
  - `sku`: regex `^[A-Z0-9]+(-[A-Z0-9]+)*$`, max 40 chars, server-side uniqueness on submit (409 → field error).
  - `name`: non-empty, max 200 chars.
  - `cat`: dropdown from existing categories (fetched from `/api/v1/inventory/categories` — Backend Gap? Or hardcoded list for Sprint-6).
  - `initial_total`: non-negative int.
  - `threshold`: non-negative int.
  - `price` / `cost`: VND amounts, non-negative; `cost` only visible to Owner (resolved Q4).
  - `alloc`: 4 channel sliders summing to ≤100%.
- **Submit:** POST `/skus` with `Idempotency-Key` header. Success → close modal + invalidate skus query + success toast. Failure → field-level errors if 4xx with validation details; toast if 5xx.
- **Category list:** for Sprint-6, hardcode the 6-8 categories from prototype `data.jsx` SKU fixtures. Sprint-7+ adds dynamic category endpoint if needed (defer).
- **Toggle primitive:** standard 2-state toggle. `--accent` background when on, `--neutral-200` when off. 18px height (desktop) per STYLING_SPECS §7.

**Patterns to follow:**
- Prototype: `screen-inventory.jsx` `<DesktopInventory>` "SKU mới" button + form modal.
- Sprint-5 `SkuFlagsController` PUT endpoint shape — frontend hits this directly (cross-module: StockSync.Api, separate route in Gateway).

**Test scenarios:**
- **Covers AE4, R10.** Click flash-sale toggle → optimistic flip → PUT `/api/v1/skus/{sku}/flag` fires. Success → toggle stays. Failure → reverts + error toast.
- **Optimistic UI**: toggle visual flips before request resolves.
- **Idempotent toggle**: clicking twice rapidly only fires 1 effective request (debounce or query mutation queue).
- **R11 Create SKU happy path**: click "SKU mới" → modal opens → fill valid form → submit → POST `/skus` → 201 → modal closes + table refreshes + new SKU visible + success toast.
- **R11 SKU validation**: invalid sku format (lowercase) → field error "SKU phải là chữ HOA + số + dấu gạch ngang".
- **R11 duplicate SKU**: server returns 409 → field error on `sku` field "SKU đã tồn tại".
- **R11 alloc validation**: sliders summing >100% → submit disabled with explainer.
- **R11 cost visibility**: Owner role sees cost field (resolved Q4); Ops Manager role would not (out of slice but assert role-based hide hook exists for Sprint-7).
- **Modal Esc closes** but warns if form dirty (per UX best practice — optional).
- **A11y**: form labels associated with inputs; error messages aria-describedby.

**Verification:** End-to-end manual: open drawer → toggle flash-sale flag on → close drawer → reopen → still on. Manual: click "SKU mới" → fill form → submit → see new SKU in table.

---

### U13. CI workflow + frontend Vitest setup + RTL + jest-axe + smoke build

**Goal:** Add frontend build job to `.github/workflows/ci.yml` running in parallel with existing dotnet jobs. Configure Vitest + React Testing Library + jest-axe (vitest-axe) for component + a11y tests. Ensure CI passes on lint + typecheck + test + build.

**Requirements:** R23 (CI integration), R24 (lockfile + `node_modules/` gitignored).

**Dependencies:** U1-U12 (all frontend code shipped; tests written alongside).

**Files:**
- `.github/workflows/ci.yml` (modified — add `frontend` job)
- `web/vitest.config.ts` (new + test setup file)
- `web/vitest.setup.ts` (new — RTL + jest-dom + jest-axe matchers)
- `web/.github/CODEOWNERS` (optional — if frontend has different owner; defer)
- `web/package.json` (modified — add scripts: `dev`, `build`, `preview`, `test`, `test:watch`, `lint`, `typecheck`, `format`)

**Approach:**
- **`vitest.config.ts`:** environment `jsdom`, setupFiles `./vitest.setup.ts`, `globals: true`, coverage via `@vitest/coverage-v8`.
- **`vitest.setup.ts`:** import `@testing-library/jest-dom`, `vitest-axe` matchers, RTL `cleanup` afterEach, `vi.useRealTimers()` default.
- **`package.json` scripts:**
  - `dev`: `vite`
  - `build`: `tsc --noEmit && vite build`
  - `preview`: `vite preview`
  - `test`: `vitest run`
  - `test:watch`: `vitest`
  - `lint`: `eslint . --max-warnings 0`
  - `typecheck`: `tsc --noEmit`
  - `format`: `prettier --write .`
- **CI yml job:**
  ```yaml
  frontend:
    runs-on: ubuntu-latest
    defaults:
      run:
        working-directory: web
    steps:
      - uses: actions/checkout@v4
      - uses: pnpm/action-setup@v3
        with: { version: 9 }
      - uses: actions/setup-node@v4
        with: { node-version: 20, cache: pnpm, cache-dependency-path: web/pnpm-lock.yaml }
      - run: pnpm install --frozen-lockfile
      - run: pnpm typecheck
      - run: pnpm lint
      - run: pnpm test --coverage
      - run: pnpm build
  ```
- Parallel with existing dotnet jobs; both must pass for PR merge.
- Coverage threshold soft (no enforcement) for Sprint-6; Sprint-7+ can tighten.

**Patterns to follow:**
- Existing `.github/workflows/ci.yml` shape for dotnet jobs.
- Vitest + RTL canonical setup from Vitest docs.

**Test scenarios:**
- **Smoke**: CI green on a clean PR.
- **TypeScript error**: introduce intentional TS error in any file → `pnpm typecheck` fails → CI red.
- **ESLint error**: unused import → `pnpm lint` fails.
- **Test failure**: introduce failing test → `pnpm test` fails.
- **Coverage report generated**: `coverage/` directory present after run.

**Verification:** PR with frontend changes shows 5 CI checks (existing dotnet + new frontend) all green. Bad PR fails appropriately.

---

### U14. Sprint-6 sign-off + CHANGELOG + README/CLAUDE update + tag

**Goal:** Close Sprint-6 with sign-off doc capturing what shipped + deviations from plan + KTD recap + tag `v0.9.0-frontend-vertical-slice`. Update README current-stage block + CLAUDE.md current-stage + sprint history. Plan frontmatter `status: active → completed`.

**Requirements:** Sign-off (origin success criteria).

**Dependencies:** U1-U13.

**Files:**
- `docs/phase-gates/2026-05-XX-sprint-6-signoff.md` (new — XX = sprint close date)
- `docs/CHANGELOG.md` (modified — append Sprint-6 entry)
- `README.md` (modified — current-stage block + badge)
- `CLAUDE.md` (modified — current-stage + sprint history shift)
- `docs/plans/2026-05-18-002-feat-sprint-6-frontend-vertical-slice-plan.md` (this file — `status: active → completed`, add `completed`, `signoff`, `tag`)

**Approach:** Mirror Sprint-5 sign-off shape:
- Frontmatter: `status: complete`, `date`, `plan`, `follows`, `tag`.
- "What shipped" U-ID status table.
- KTD recap (KTD1-9 from this plan).
- Deviations from plan (anywhere implementation drifted).
- Documented limitations / carried-forward deferrals:
  - Sprint-7 takes over real auth + real SignalR + Dashboard or Orders screen
  - Sprint-5.5 scale-gate harness still deferred
  - 11 Backend Gaps still open
  - Inbound module UI still not designed
- Test counts: backend unit/integration deltas + frontend unit/component test count.
- Frontend bundle size measured (e.g., "main bundle 245kb gzipped").
- Visual reference: screenshot of Inventory screen (optional; if Docker daemon + frontend running locally).
- "Next implementation step" pointing at Sprint-7 candidates.

CHANGELOG entry under `## 2026-05-XX — Sprint-6 Frontend Vertical Slice complete`. Mirror Sprint-5 entry shape.

README + CLAUDE current-stage: rewrite to "Sprint-6 complete" pointing at sign-off + describing what shipped at high level. Move Sprint-5 paragraph to history block.

**Patterns to follow:**
- `docs/phase-gates/2026-05-17-sprint-5-signoff.md` — sign-off doc shape.
- `docs/phase-gates/2026-05-18-methodology-writeup-signoff.md` — recent precedent.
- Sprint-5 README + CLAUDE.md current-stage update pattern from `9132164` commit.

**Test scenarios:** `Test expectation: none — documentation + tag unit.` Verify: sign-off doc reads end-to-end without TODO markers; README badge points correctly; tag `v0.9.0-frontend-vertical-slice` exists annotated; CHANGELOG entry covers all 14 units.

**Verification:** `git tag --list "v0.9.0-frontend-vertical-slice"` exists; sign-off doc + CHANGELOG + README + CLAUDE all consistent. Manual review of methodology pattern continuity (Sprint-2.5 / 4.5 / 5.5 / 6 cadence visible in CLAUDE history block).

---

## System-Wide Impact

| Surface | Impact | Owning unit |
|---|---|---|
| **`web/` subdirectory** | New top-level frontend project. Vite + pnpm + TypeScript + React 19. ~50 new source files at sprint close. | U1, U2, U3, U5-U12 |
| **`src/Services/Auth/` 4-csproj quartet** | New module quartet (stub-grade) — `ShopFlow.Auth.{Domain,Application,Infrastructure,Api}`. Sprint-7 fills real implementation. | U4 |
| **`src/Services/Inventory/ShopFlow.Inventory.Api`** | Add 5+ controllers (SkusController, AdjustmentsController, InventoryController) + MediatR queries/commands. JwtBearer registered. | U7, U8 |
| **`src/Services/Inventory/ShopFlow.Inventory.Application/Queries` + `Commands`** | New MediatR handlers (3 queries, 3 commands). | U7, U8 |
| **`src/Services/Inventory/ShopFlow.Inventory.Infrastructure/Migrations`** | New migration: `inventory_idempotency_records` table. | U8 |
| **`src/ApiGateway/ShopFlow.Gateway/appsettings.json`** | Add routes for `/auth/**` + `/api/v1/inventory/**`. | U4 |
| **`src/AppHost/ShopFlow.AppHost/Program.cs`** | Register `auth-api` resource. | U4 |
| **`ShopFlow.sln`** | Add 4 Auth.csproj + (no Auth.UnitTests for stub; 1 controller smoke test only) = 5 entries. | U1, U4 |
| **`.github/workflows/ci.yml`** | Add `frontend` job (parallel). | U13 |
| **`.gitattributes`** | New at repo root. CRLF normalisation closes friction mode 6. | U1 |
| **`README.md`** | Current-stage block updated. Badge updated. Inter/Inter Tight references removed (U2). | U2, U14 |
| **`CLAUDE.md`** | Current-stage block updated. Sprint-5 paragraph relocated to history. Sprint-6 history added. | U14 |
| **`docs/CHANGELOG.md`** | New Sprint-6 entry. | U14 |
| **`docs/phase-gates/`** | New Sprint-6 sign-off doc. | U14 |
| **No `src/Services/Inbound`, `Outbound`, `Channel`, `StockSync` changes** | Sprint-5's `SkuFlag` PUT endpoint already exposed; no other backend changes. | (none) |

---

## Dependencies / Prerequisites

- **Branch from `v0.8.0-methodology-writeup`** — fresh branch `feat/sprint-6-frontend-vertical-slice`.
- **Node.js 20+ LTS and pnpm 9+ on dev machine** — currently dev machine has Node 20 (verified via Aspire's existing tooling, but pnpm may need install: `npm install -g pnpm@9`).
- **.NET 9 SDK available on CI** — already confirmed for backend builds; Auth.Api + Inventory.Api updates compile on CI.
- **Docker daemon** — for integration tests in U7, U8 (Testcontainers Postgres). Continues Sprint-1..5 pattern: dev machine integration tests run when daemon up; CI runs them on every PR.
- **No external dependencies added** beyond pnpm packages declared in U1.
- **Existing AGENTS.md / CLAUDE.md** carry context; do not modify until U14.

---

## Risk Analysis & Mitigation

| Risk | Likelihood | Impact | Mitigation |
|---|---|---|---|
| **pnpm or Node not installed on dev machine; can't run frontend locally** | Medium | High | U1 includes install verification step + README quickstart. Worst case: rely on CI to validate (Sprint-1..5 precedent for .NET 9 SDK gap). |
| **TanStack Router v1 file-based routing learning curve** | Medium | Medium | Allocate extra time in U6; fallback to React Router 6 if Router blocks progress. Plan-time decision (KTD1) can be revisited in U6 if needed. |
| **JWT validation + tenant-routing-middleware-claim alignment subtle bugs** | Medium | High | U4 test scenarios + U7 integration test ("wrong tenant_slug → 403") catches misalignment. Existing `TenantRoutingMiddleware` tests from Phase-0-redux U4 already validate the priority rules. |
| **Idempotency table contention under load** | Low | Medium | Sprint-6 has 1 user (dev mode) — no contention. Sprint-7+ load tests if needed; index hint on `(tenant_id, idempotency_key)` is the standard fix. |
| **Plex Sans Vietnamese subset incorrect — diacritics missing** | Low | High | U2 visual test for Vietnamese rendering. STYLING_SPECS §2.1 confirms Plex Sans Vietnamese coverage; failure mode would be specific characters clipping. |
| **CRLF/LF noise persists despite `.gitattributes`** | Low | Low | U1 + repo `re-normalize`: `git add --renormalize .` after `.gitattributes` lands. One-time cleanup. |
| **Inventory query polling hammers backend** | Low | Low | TanStack Query auto-pauses on inactive tab; 2s interval at single dev user is < 1 req/sec average. Production scale-out is Sprint-7+ SignalR. |
| **Frontend bundle size balloons past target** | Low | Low | Vite tree-shaking + code-splitting for routes. Target: main bundle < 300kb gzipped. Measured in U14 sign-off. |
| **`<ComingSoon>` component feels too placeholder-y for portfolio demo** | Medium | Low | KTD7 — invest 30min in 1 high-quality variant; doesn't need to be heroic. Sprint-7+ adds polish. |
| **A11y contrast fixes break existing prototype visuals** | Low | Medium | U2 visual diff check vs prototype; `--ink-3` re-point is subtle (still gray, just darker). |
| **Sprint scope creep — adding 5th write or another screen mid-sprint** | Medium | Medium | Scope discipline: 4 writes named in R8-R11; any addition is Sprint-7+ work. Plan's "Deferred to Follow-Up Work" anchors this. |
| **Mid-sprint KTD emergence (like Sprint-5 KTD7)** | Medium | Medium | Expected per methodology writeup; sign-off captures honestly. Mitigation: explicit "scope/lifetime/tenant-context" review at U6 (routing + auth context) and U11 (write idempotency). |
| **Sprint-5.5 scale-gate harness still unbuilt** | n/a | n/a | Not in scope for Sprint-6. Continues to defer. Methodology writeup friction mode 4 already documents kicking-the-can analysis. |

---

## Alternative Approaches Considered

### A1 — Real auth module + real SignalR hub in Sprint-6 (Option B from brainstorm)

**What it looks like:** Pre-Sprint-6 prereq sprint (Sprint-5.6) builds real auth module + SignalR hub before frontend. Then Sprint-6 is purely frontend wired against real backend.

**Why rejected:** Adds ~1.5 weeks to total time before user sees any UI. Methodology writeup's deferral pattern (Sprint-2.5 / 4.5 / 5.5) proves the alternative works. User explicitly chose hybrid in brainstorm dialogue.

### A2 — Separate frontend repo (not `web/` subdirectory)

**What it looks like:** `shopflow-wms-web` repo cut from `shopflow-wms` via `git subtree split`. Independent CI, deployment, versioning.

**Why rejected:** STYLING_SPECS §6 + brainstorm KTD: same-repo aligned with solo-dev + methodology emphasis + single commit history. Splitting can happen later if needed.

### A3 — Skip vertical slice; do horizontal scaffold across 5 screens

**What it looks like:** Sprint-6 ships shell + tokens + auth + 5 placeholder screens; Sprint-7 wires data per-screen.

**Why rejected:** Loses the "prove integration end-to-end" demo. 1 screen × 1 role fully wired is more methodology-honest than 5 screens half-done. Confirmed via brainstorm framing decision.

### A4 — Frontend in Blazor / .NET WASM (single language)

**What it looks like:** No JS/TS toolchain; Blazor Server or Blazor WASM with C#.

**Why rejected:** Design handoff explicitly recommends React + TypeScript + Vite + token-based CSS. Blazor would require redesigning + losing the prototype's design vocabulary. Larger bundle, worse DX, smaller ecosystem.

---

## Documentation Plan

- This plan file — primary reference.
- `docs/brainstorms/2026-05-18-sprint-6-frontend-vertical-slice-requirements.md` — origin.
- `docs/phase-gates/2026-05-XX-sprint-6-signoff.md` — sign-off (U14).
- `docs/CHANGELOG.md` — Sprint-6 entry under `[0.9.0-frontend-vertical-slice]` (U14).
- `README.md` + `CLAUDE.md` — current stage update (U14).
- `web/README.md` — frontend-specific quickstart for `pnpm install && pnpm dev` (U1).
- `docs/solutions/` — potential entries for non-obvious learnings (e.g., TanStack Router file-based-routing gotchas, JWT validation quirks).
- ADR-0001 (Aspire) — unaffected. ADR-0002 (Modular Monolith) — unaffected; frontend doesn't change backend architecture. ADR-0003 (DB-per-tenant) — unaffected; frontend respects tenant routing via JWT.

---

## Operational / Rollout Notes

- Sprint-6 ships as a new `web/` subdirectory + new backend Auth.Api stub. No production deployment changes.
- Aspire AppHost adds `auth-api` resource; `task up` (when Docker available) starts both backend + frontend dev mode (frontend separate via `pnpm dev` in `web/`).
- Gateway YARP routes for `/auth/**` + `/api/v1/inventory/**` ship inline in `appsettings.json`.
- Existing tenants (dev1, dev2) gain `inventory_idempotency_records` table via tenant provisioning migration re-apply: `shopflow-migrate apply <tenant>`.
- Feature flag: none for this sprint.

---

## Future Considerations

- **Sprint-7 — real auth module + real SignalR hub + 2nd screen (Dashboard or Orders)** — natural next sprint. The fake auth swap is the headline; SignalR hub swap is the secondary. Adds 2nd vertical slice with 2nd role unlocked.
- **Sprint-8 — Orders + Channels screens, Operator role enabled (768px breakpoint, mobile-first pick-wave UI)**.
- **Sprint-9 — Compliance + Audit screens** (RTBF modal + sub-processor list + audit-event drawer with JSON diff).
- **Sprint-10 — Onboarding wizard + Settings tier IA + Tenants Admin**.
- **Sprint-11+ — Inbound module UI design + implementation** (INTEGRATION §10 notable cut).
- **Sprint-5.5 — Scale-gate harness closure** continues to be a separate deferral; can run parallel with frontend phase if capacity allows.
- **Phase-3 polish** — Lazada / TikTok adapters, observability dashboards (Grafana / Prometheus), portfolio README + demo video, deployment docs.

---

## Outstanding Questions

### Resolve Before Implementation

*(rỗng — all product decisions captured in origin doc + KTD1-9 above)*

### Deferred to Implementation

- [Affects U1][Technical] Vite plugin React Compiler RC — opt-in if stable enough by sprint start; otherwise stick with `@vitejs/plugin-react-swc`.
- [Affects U3][Technical] Logo SVG generation tool — Figma export vs hand-coded `<rect>` grid. Hand-coded is preferred for fidelity since dot-matrix is geometric.
- [Affects U3][Technical] Favicon generation toolchain — `pnpm dlx @realfavicon/cli` vs `pnpm dlx pwa-asset-generator` vs Squoosh manual. Implementer picks based on what's smoothest.
- [Affects U5][Technical] `jwt-decode` package vs hand-rolled base64-url decoder. Hand-rolled is ~5 lines; package is ~1KB. Implementer picks.
- [Affects U7][Needs research] Aggregate query for `/inventory/summary` — single SQL with subqueries vs multiple round-trips. Performance test on seeded data with 5000 SKUs.
- [Affects U8][Technical] `inventory_idempotency_records` table TTL — should rows expire after some window (e.g., 30 days)? Plan defers — Sprint-7+ can add a cleanup background job if table size becomes an issue.
- [Affects U8][Technical] `RequireIdempotencyKey` attribute — implement as middleware vs `[ServiceFilter]`. Both work.
- [Affects U9][Technical] Filter URL persistence — TanStack Router's `useSearch` is the canonical pattern; confirm in U9.
- [Affects U10][Technical] Drawer width breakpoint — 620px fixed (per STYLING_SPECS) or fluid 50vw on wide screens?
- [Affects U11][Technical] Toast positioning — STYLING_SPECS says bottom-right; confirm stacking order for multiple toasts.
- [Affects U12][Technical] Category list source — hardcoded enum vs new `/api/v1/inventory/categories` endpoint. Hardcoded for Sprint-6; Sprint-7+ promotes if needed.
- [Affects U13][Technical] Coverage threshold — set in Sprint-7 once frontend has more surface; Sprint-6 ships coverage report without enforcement.

### Deferred to Follow-Up Sprint

- Real Auth module (Sprint-7): swap fake `Auth.Api` Controllers for real login + JWT issuance + refresh token rotation + Redis denylist + TOTP MFA + per-tenant member store.
- Real SignalR hub (Sprint-7): replace TanStack Query polling with WebSocket subscriptions per INTEGRATION §5 14 event types.
- Dashboard or Orders screen (Sprint-7): unlock 2nd screen; pick based on what unblocks the next demo flow.
- Operator role + 768px breakpoint + mobile-first pick-wave UI (Sprint-8).
- Compliance + Audit + Onboarding + Settings screens (Sprints 9-10).
- Inbound module UI design + implementation (Sprint-11+).
- Sprint-5.5 scale-gate harness closure (still deferred).
