---
title: "Sprint-7 sign-off — Orders Saga Visualisation (Second Vertical Slice + SignalR)"
date: 2026-05-19
status: complete
follows: docs/phase-gates/2026-05-19-sprint-6-signoff.md
plan: docs/plans/2026-05-19-001-feat-sprint-7-orders-saga-visualisation-plan.md
tag: v0.10.0-sprint-7-orders
---

# Sprint-7 sign-off — Orders Saga Visualisation (Second Vertical Slice + SignalR)

Sprint-7 ships ShopFlow WMS's **second frontend vertical slice** — an Orders saga-visualisation surface that watches a customer order traverse the 11-state fulfillment saga (Placed → Reserved → AwaitingPick → … → Shipped) with sub-second freshness via SignalR push. The slice closes Sprint-6 trade-off #9 (polling → SignalR) and surfaces Sprint-3-redux's saga state machine at the UI layer for the first time. 14 implementation units (U1-U14) shipped on `feat/sprint-7-orders-saga-visualisation` cut from `v0.9.0-frontend-vertical-slice`.

The slice proves the methodology pattern generalises: same brainstorm → doc-review → plan → 14 units → per-unit commit → sign-off + tag cadence as Sprint-6, but executed across **three parallel-dispatch rounds** (5 + 3 + 2 subagents) plus inline turns. The orchestrator-reviews-each-diff pattern caught zero behavioural regressions while collapsing wall-time from a fully-serial implementation.

## What shipped

| U-ID | Goal | Status | Commit |
|------|------|--------|--------|
| U0 | Branch cut + opening commit with brainstorm + plan + 5 doc-review architectural decisions in body | ✅ | `1d1c5b9` |
| U1 | `outbound_saga_transitions` audit table + `OrderTransition` entity + `IOrderTransitionRepository` + EF entity config + migration `20260519100001_AddOrderTransitions` (`[Migration]` + `[DbContext]` per AGENTS.md §3.23). **`correlation_id` column added per doc-review fix** | ✅ | `7be1852` |
| U2 | `SagaTransitionedV1` cross-module integration contract + `SagaTransitionObserver` class + explicit per-branch `.ThenAsync(ctx => RecordTransitionAsync(...))` hooks at every TransitionTo site in `FulfillmentSaga` (9 transitions instrumented incl. `WhenEnter` IfElse Path A + `If` counter-drain) | ✅ | `b854524` |
| U3 | Outbound MediatR queries — `ListOrdersHandler` (filter+page, MAX-join for LastTransitionAt, WHERE-IN saga_state lookup), `GetOrderDetailHandler` (Result.Failure on not-found), `GetOrderTransitionsHandler` | ✅ | `d78bea6` |
| U5 | SharedKernel SignalR foundation — `TenantHub` + `TenantBindingHubFilter` (mirrors K12) + `MapShopFlowHubs` extension + `AddSignalR` + **JwtBearer lifted to kernel** + **access-token query redaction (doc-review SEC-001)** + Auth config propagated to Outbound/Inbound/Channel/StockSync appsettings | ✅ | `4317ba5` |
| U7 | Frontend SignalR client (`signalr.ts`) + `useSignalR` Zustand hook (TEST-FIRST; 13 scenarios) + `@microsoft/signalr ^8.0.7` dep | ✅ | `e79212a` |
| U11 | `<SagaPipeline>` horizontal 8-node component + tokens.css `.saga-step.completed` + `.saga-failure-caption` rules + `aria-current="step"` inside `role="list"` (doc-review design-lens #8) | ✅ | `b35ed33` |
| U12 | `<TransitionsLog>` newest-first feed with `aria-live="polite"` (doc-review design-lens #9) + Cancelled-row failure styling | ✅ | `fb79a79` |
| U4 | Outbound `OrdersController` 4 new endpoints (`GET /`, `GET /kpis`, `GET /{id}/transitions`, `POST /seed` `[Idempotent]` dev-mode-only) + 6 new DTOs + MediatR `assembliesToScan` fix in Program.cs + 9-arg ctor with 7-arg backward-compat | ✅ | `a59188e` |
| U6 | SignalR relay consumers (`StockChangedRelayConsumer`, `SagaTransitionedRelayConsumer`) in SharedKernel.Infrastructure.SignalR/ — **registered ONLY on Outbound.Api via `AddOutboundModule`** per single-hub-host topology | ✅ | `507826c` |
| U9 | Inventory `useInventoryQuery` SignalR wire-up — `useStockChangedSubscription` helper + dynamic `refetchInterval` toggle on hub state + R13 polling fallback intact | ✅ | `96aecdd` |
| U8 | Frontend Orders API surface (`api/orders.ts` + 7 PascalCase DTOs) + `useOrdersListQuery` / `useOrderKpiQuery` / `useOrderDetailQuery` / `useOrderTransitionsQuery` (narrow-invalidate on matching OrderId) + `useSeedOrderMutation` (TEST-FIRST per plan execution note) | ✅ | `f1bf866` |
| U10 | `/orders` list route + `OrdersKpiStrip` + `OrdersFilterStrip` + `OrdersTable` (KTD11 cell-level button) + `SeedTestOrderButton` (DEV-only, disabled-while-pending) + Sidebar rename (outbound→orders, icon Receipt→ShoppingBag) + outbound.tsx soft-deleted via beforeLoad redirect | ✅ | `8c00555` |
| U13 | `/orders/$orderId` detail route — composes `<SagaPipeline>` + `<OrderLineItems>` (KTD11) + `<TransitionsLog>` + LedgerDrawer reuse via `SkuListItem` adapter; `inferFailureCause` walks transitions backwards from `→ CompensatingReservation` | ✅ | `8b3ee6b` |
| U14 | A11y axe-smoke harness extended (4 new cases) + this sign-off + CHANGELOG entry + README + CLAUDE.md current-stage update + annotated tag `v0.10.0-sprint-7-orders` | ✅ | (this commit) |

## Stack & infrastructure delta

| Surface | Change |
|---|---|
| `src/Services/Outbound/ShopFlow.Outbound.Domain` | +`OrderTransition` entity |
| `src/Services/Outbound/ShopFlow.Outbound.Application` | +`IOrderTransitionRepository` port; +3 MediatR query handlers; +`SagaTransitionObserver` class; saga `FulfillmentSaga.cs` instrumented with `.ThenAsync` hooks at every TransitionTo (9 transitions); +`MediatR` PackageReference |
| `src/Services/Outbound/ShopFlow.Outbound.Infrastructure` | +`OrderTransitionConfiguration` EF map; +`OrderTransitionRepository`; +migration `20260519100001_AddOrderTransitions`; `OutboundDbContext` adds `DbSet<OrderTransition>`; `OutboundServiceCollectionExtensions` registers observer + outbox route for `SagaTransitionedV1` + `AddConsumer<>()` calls for both relay consumers (single-hub-host) |
| `src/Services/Outbound/ShopFlow.Outbound.Api` | `OrdersController` +4 endpoints + `[Authorize]` class-level + 9-arg primary ctor + 7-arg legacy-compat ctor; +6 new DTOs in `OrderDtos.cs`; `Program.cs` adds Outbound.Application assembly to MediatR scan + `UseAuthentication/UseAuthorization` + `MapShopFlowHubs()` + `public partial class Program;` |
| `src/Services/Inventory/ShopFlow.Inventory.Api` | -AddJwtBearer block (now in kernel); -duplicate JwtBearer PackageReference |
| `src/Shared/ShopFlow.Contracts` | +`SagaTransitionedV1` record in `Contracts/Outbound/` |
| `src/Shared/ShopFlow.SharedKernel` | NEW `Infrastructure/SignalR/` directory: `TenantHub` + `TenantBindingHubFilter` + `SignalRRoutingExtensions` + `HubEventPayloads` + `StockChangedRelayConsumer` + `SagaTransitionedRelayConsumer`. `AddShopFlowDefaults` adds `services.AddSignalR()` + JwtBearer registration + access-token query handler with redaction. +`Microsoft.AspNetCore.Authentication.JwtBearer` PackageReference |
| `src/ApiGateway/ShopFlow.Gateway/appsettings.json` | +`/hub` + `/hub/{**catch-all}` routes → outbound cluster (negotiate + WS upgrade coverage) |
| Module `appsettings.json` (Outbound/Inbound/Channel/StockSync) | +`Auth:DevSecret/Issuer/Audience` config section (kernel now throws on missing) |
| `web/` | New top-level `_auth/orders/` route directory (`index.tsx` + `$orderId.tsx`); 7 new components under `components/orders/`; 3 new hooks (`useSignalR`, `useOrdersQuery`, `useOrderMutations`); 2 new lib files (`signalr.ts`, `api/orders.ts`); Sidebar + screenPaths renamed outbound→orders; `outbound.tsx` rewritten as redirect; `tokens.css` extended with `.saga-step.completed` + `.saga-failure-caption` |
| `web/package.json` | +`@microsoft/signalr ^8.0.7` |

## Test count

| Tier | Sprint-6 baseline | Sprint-7 added | Sprint-7 total |
|------|---|---|---|
| Backend Unit | 361 | +33 (16 query handlers + 4 SagaTransitionObserver + 5 TenantBindingHubFilter + 3+4 relay consumers + 1 OrderTransition entity) | 394 |
| Backend Integration | 24 | +18 (6 OrderTransitionRepository + 3 SagaTransitionsAuditFlow + 1 SagaTransitionsEndToEndSignalR + 8 OrdersListAndDetailEndpoint + 6 OrdersSeedEndpoint — minus overlap) | 42 |
| Frontend Vitest | 221 | +130 (13 useSignalR + 24 useOrdersQuery + 9 useOrderMutations + 16 useInventoryQuery + 17 SagaPipeline + 5 TransitionsLog + 8 OrdersTable + 5 OrderLineItems + ~33 other component/test additions) | ~351 |
| Frontend test files | 27 | +9 | ~36 |
| A11y axe assertions (smoke) | 6 | +4 (SagaPipeline happy+failure, TransitionsLog populated+empty, OrderLineItems) | 10 |

Test execution still runs in CI on push (Sprint-1 established posture; this dev machine has .NET 8.0.407 vs the repo's pinned 9.0.305). Frontend tests run locally without infra deps.

## Key technical decisions

**KTD1 — Brainstorm framing pivot**: brainstorm presented three Sprint-7 options (Real auth focused / Polish bundle / Sprint-7-as-written / Orders alt). Picked Orders alt for highest portfolio leverage (second slice proves methodology generalises), then folded SignalR push *into* this sprint after the reactivity dialogue showed polling would visually skip saga transitions (Reserved → AwaitingPick happens in ~50ms within one consumer pass).

**KTD2 — Single hub-host topology** (doc-review adversarial #1): only Outbound.Api maps `/hub` + registers relay consumers; other module APIs (Inventory/Inbound/Channel/StockSync) intentionally don't. Gateway routes `/hub` + `/hub/{**catch-all}` → outbound cluster as the permanent decision. Rationale: avoids the RabbitMQ competing-consumer trap on the eventual W6 split where each event would land on one arbitrary process while the connected client lives elsewhere. Auth.Api also excluded (intentionally skips `AddShopFlowDefaults`, so `AddSignalR` isn't wired; mapping the hub there would throw at startup — the doc-review safe_auto fix corrected this).

**KTD3 — IStateObserver via explicit per-branch `.ThenAsync` wiring** (doc-review adversarial #2): the user-chosen "IStateObserver<T>" interface mechanism shipped as a class with explicit per-branch wiring rather than MT's `IStateObserver<TInstance>` interface itself. MT 8.3.4 doesn't reliably expose `IStateObserver<T>` through `MassTransitStateMachine<T>`, and backend tests run only in CI on this dev machine — writing a wrong observer-connector pattern that compiled but didn't fire would have been worse than explicit branch coverage. Class-shape decision preserved (single observer class, comprehensive branch coverage). Branch coverage *exceeds* what MT's auto-observer would catch: the `WhenEnter(CompensatingReservation)` `IfElse` Path A short-circuit, the `During(CompensatingReservation).When(StockReleased).If(<=0)` counter-drain, and the compound `StockReserved → Reserved → AwaitingPick` chain (two state changes in one consume) are all explicitly instrumented.

**KTD4 — `correlation_id` schema gap closure**: origin R14 specified the column; plan KTD already committed to W3C TraceContext propagation; `SagaTransitionedV1` contract already carried `CorrelationId` — but the plan U1 schema + entity omitted it. Doc-review coherence reviewer caught the omission; orchestrator declared resolved at execution start and U1's schema includes it. Bonus: subagent dispatch later used the corrected schema as the source of truth.

**KTD5 — `IOutboundOutbox.AppendAsync` as the audit-event publish mechanism**: plan text said "via the existing `AppendOutbox<T>` helper on `OutboundDbContext`" but no such helper exists. Outbound's actual outbox surface is `IOutboundOutbox.AppendAsync(string eventType, object payload, ct)`. Doc-review feasibility reviewer caught it; SagaTransitionObserver uses the existing surface unchanged.

**KTD6 — JwtBearer lifted to kernel** (closes Sprint-6 trade-off #8 in spirit; real auth still Sprint-8). `AddJwtBearer` moved INTO `AddShopFlowDefaults`; removed from Inventory.Api Program.cs + Inventory.Api csproj. Auth config section propagated to all module APIs. **Access-token query-parameter redaction** (doc-review SEC-001): `JwtBearerEvents.OnMessageReceived` copies `?access_token=` to `context.Token` for `/hub` paths, then rebuilds `QueryString` excluding `access_token` so request logging never captures the bearer credential.

**KTD7 — Subagent dispatch shape**: Round 1 fired 5 subagents (U3, U5, U7, U11, U12 — all independent); Round 2 fired 3 (U4, U6, U9); Round 3 single (U8 — gates Round 4); Round 4 fired 2 (U10, U13). Each subagent received the plan path + the 5 doc-review architectural decisions verbatim + reference commit hashes + explicit "do not commit, do not run tests" guard. Shared-directory mode (worktree isolation skipped since `.claude/worktrees/` not yet gitignored; safe because Round-1 collision check passed). Orchestrator reviewed each diff + committed serially after each batch.

**KTD8 — `OrdersController` 7-arg backward-compat ctor**: new 9-arg primary ctor (`[ActivatorUtilitiesConstructor]`) takes `IMediator` + `IHostEnvironment`; legacy 7-arg ctor chains to it with stubs (`LegacyTestUnsupportedMediator`, `LegacyTestHostEnvironment`) so 7 pre-existing Sprint-3-redux test files (OrdersControllerTests, SagaHappyPathTests, etc.) compile + run unchanged. Avoids ballooning the diff.

**KTD9 — `RecordTransitionAsync` uses `GetService<>` (nullable)** rather than `GetRequiredService<>`. Sprint-3-redux's `FulfillmentSagaTests` builds the MT TestHarness without registering the Sprint-7 observer chain; re-wiring every legacy saga test to register `SagaTransitionObserver` + `IOrderTransitionRepository` + `IOutboundOutbox` + `TimeProvider` would have rippled across multiple test files. Production registration via `AddOutboundModule` is load-bearing; `SagaTransitionsAuditFlowTests` integration test catches the silent-failure risk.

**KTD10 — Sprint-6 `web/src/routes/_auth/outbound.tsx` rewritten as `beforeLoad` redirect to `/orders`** (subagent had no Delete tool). Bonus: stale bookmarks degrade gracefully. `routeTree.gen.ts` auto-regenerates at build to register both `_auth/orders/index.tsx` (U10) and `_auth/orders/$orderId.tsx` (U13).

**KTD11 — `LedgerDrawer` adapter pattern** at the Orders detail route: drawer's prop is `item: SkuListItem | null` (Sprint-6 inventory shape), so `$orderId.tsx` builds a minimal `SkuListItem` stub from just the clicked line's SKU (`Allocations: []`, `IsFlashSale: false`). The drawer's internal `useSkuLedgerQuery(sku)` is the real data source. No new ledger view shipped; Sprint-6 primitive reused via stub.

**KTD12 — Channel display parsed from `Order.ChannelExternalOrderId` prefix** (`SHOPEE_*` → "Shopee", `LAZADA_*` → "Lazada", `TIKTOK_*` → "TikTok Shop", else "Direct"). No new `ChannelType` column on `orders`. Defers a second migration; consistent with Sprint-6 trade-off #1 deferral pattern.

## Deviations from plan file list

- **U2 IStateObserver mechanism** — plan said "IStateObserver<T> interface registered on saga state machine"; shipped as class-with-explicit-per-branch-wiring per KTD3 above. Class-shape decision preserved.
- **U5 JwtBearer to kernel** — plan U5 said "consider lifting JwtBearer to kernel"; the doc-review JWT decision made this mandatory. Lifted in this sprint.
- **U6 single-hub-host** — plan U6 said "registered via AddShopFlowDefaults" (kernel-wide); doc-review adversarial decision moved registration to `AddOutboundModule` only. Single-host topology shipped.
- **U10 — `SeedTestOrderButton.tsx` ownership moved from U13 to U10** — plan put the file in U13's list "for cohesion" but it renders on U10's list route. Parallel-dispatch safety: U10 owns it.
- **U10 outbound.tsx delete → redirect** — subagent had no Delete tool. Rewrote as `beforeLoad` redirect; functionally equivalent + graceful for stale bookmarks. KTD10.
- **U13 LedgerDrawer reuse via SkuListItem stub** — plan didn't specify the adapter; the subagent caught the inventory-shape prop and shipped the cleanest reuse. KTD11.
- **U4 backward-compat 7-arg ctor** — plan said "modify OrdersController". The subagent kept 7 pre-existing test files green by adding a chain ctor rather than touching them. KTD8.
- **No Postgres `UNIQUE` constraint on `outbound_saga_transitions`** — plan risks table accepted the double-audit-write risk under MT redelivery; UNIQUE on `(order_id, occurred_at, to_state)` is a Sprint-7.5 follow-up if it surfaces.
- **`RecordTransitionAsync` uses GetService (nullable)** — KTD9; avoids re-wiring Sprint-3-redux's 8+ saga test files.
- **Backend builds run in CI only** — same Sprint-1+ posture. Field-shape drift on `StockReservedV1` / `StockReservationFailedV1` contract types referenced in U2's integration tests will surface in CI if it exists.

## Sprint-7 trade-offs locked in for downstream sprints

These carry into Sprint-7.5 / Sprint-8 work; restating so future units don't try to "fix" them:

1. **No Postgres UNIQUE on `outbound_saga_transitions`** — double-audit-write under MT redelivery accepted; UNIQUE follow-up only if observed in production.
2. **No `ChannelType` column on `orders`** — channel display parsed from external order id prefix.
3. **Channel allocation + p24 outbound** still ship as zero/empty (Sprint-6 trade-off #3 unchanged).
4. **URL-search-params persistence for filter state** — still local React state (Sprint-6 trade-off #4 unchanged); Sprint-7.5 candidate.
5. **camelCase wire normalisation** — still deferred; new Sprint-7 endpoints ship PascalCase consistent with Sprint-6 KTD4.
6. **Reservation ledger cursor pagination** — still deferred (Sprint-6 trade-off #5).
7. **Cosmetic SKU schema expansion** (name/category/threshold columns) — still in `InMemorySkuMetadataStore` singleton (Sprint-6 trade-off #1); Sprint-7.5 candidate.
8. **Flash-sale dual-write to StockSync** — still single-endpoint (Sprint-6 trade-off #10); Sprint-7.5 candidate.
9. **Real auth** — JwtBearer lifted to kernel + access_token redaction in place, but the dev-mode baked JWT remains (Sprint-6 trade-off #8). Real password hashing + refresh tokens + role claims + MFA is Sprint-8.
10. **Polling fallback intact** (R13 satisfied; Sprint-6 KTD5 preserved).
11. **SagaPipeline `Created` + `AwaitingReservation` collapse into "Placed"** — operator observability of the AwaitingReservation latency wait is degraded (doc-review adversarial #7 advisory; UX call). 9-node pipeline that splits them is a Sprint-7.5 candidate if operations want to see slow Inventory consumers.
12. **`SagaTransitionedV1` outbox-routed Publish** — direct publish-skip-outbox might shave ~poll-interval latency (doc-review adversarial #5 advisory). Accepted for Sprint-7; revisit if AE3's "~200ms hub event" target ever drifts.

## Carried-forward deferrals from prior sprints

- **Sprint-5.5 scale-gate harness** — multi-tenant Aspire boot + real Shopee mock alongside StockSync.Api. Same Sprint-4 U9 / Sprint-4.5 / Sprint-5 U9 precedent. Not blocking Sprint-7; runs orthogonal.
- **Sprint-6 vitest-axe@0.1.0 type shim** — KTD12 from Sprint-6 still in place (`web/src/types/vitest-axe.d.ts`). Sprint-7 inherits the shim.
- **CSharpier formatting cleanup** carried from Phase-0-redux U10 — 23 files still drift. CI's `csharpier --check` will block on first run; one cleanup commit fixes them. Sprint-7 didn't address.

## Vercel skills applied

`vercel-react-best-practices` + `vercel-composition-patterns` + `web-design-guidelines` (Sprint-6 install) applied to Sprint-7's frontend additions:
- `rerender-derived-state-no-effect` — Sprint-7 components use `useMemo` for derived state throughout (TransitionsLog newest-first sort; SagaPipeline elapsed-time computation; OrdersTable status-to-pill-kind mapping).
- `web-design-guidelines` — `aria-live="polite"` on TransitionsLog (doc-review design-lens #9); `aria-current="step"` on SagaPipeline active node inside `role="list"` parent (doc-review design-lens #8); KTD11 cell-level button on OrdersTable + OrderLineItems; `htmlFor` on form labels in OrdersFilterStrip.

## Doc-review pipeline execution

Sprint-7 ran `/ce-doc-review` on the plan before execution. 5 reviewers dispatched (coherence + feasibility always-on; design-lens + security-lens + adversarial conditionally activated). 17 actionable findings (5 P1 + 12 P2) + 8 FYI observations + 1 `safe_auto` fix applied.

**Safe_auto fix applied** (`d:\shopflow-wms\docs\plans\2026-05-19-001-feat-sprint-7-orders-saga-visualisation-plan.md`):
- Dropped Auth.Api from U5's `MapShopFlowHubs` target list — Auth.Api intentionally skips `AddShopFlowDefaults` (per its own banner comment) so `AddSignalR` isn't wired; mapping the hub there would throw at startup. Two reviewers agreed (feasibility + adversarial).

**Three P1s declared resolved by orchestrator** at execution start (no real architectural ambiguity):
- `correlation_id` column added to `outbound_saga_transitions` (origin R14 + W3C TraceContext per AGENTS.md §6.43).
- `IOutboundOutbox.AppendAsync` as the audit-event publish mechanism (existing surface; not a new `AppendOutbox<T>`).
- JwtBearer lifted to `AddShopFlowDefaults` (closes Sprint-6 duplication).

**Two architectural premise findings resolved by user-decision** at execution start (genuinely ambiguous):
- **Hub topology**: single hub-host process (Outbound.Api only) vs Redis backplane. Chose single-host; lower scope, defers backplane question.
- **Audit-write mechanism**: IStateObserver vs per-Then with full branch coverage. Chose IStateObserver intent, shipped as explicit-wiring per KTD3 (MT 8.3.4 API limitation).

All five decisions recorded in `1d1c5b9`'s commit body for downstream agents reading the plan.

## Branch + tag + commit chain

- Branch: `feat/sprint-7-orders-saga-visualisation` (cut from `v0.9.0-frontend-vertical-slice`).
- Tag: `v0.10.0-sprint-7-orders` (annotated; this commit).
- Push: deferred — dev machine offline at sprint-close time; user memory entry says push-before-branch-cut and push-after-sprint-close.
- Commit chain (15 commits incl. U0):
  - `1d1c5b9` U0 (docs: brainstorm + plan + decisions)
  - `7be1852` U1 (audit table)
  - `b854524` U2 (SagaTransitionedV1 + observer + saga wiring)
  - `d78bea6` U3 (MediatR queries)
  - `4317ba5` U5 (SharedKernel SignalR + JwtBearer kernel-lift)
  - `e79212a` U7 (frontend useSignalR hook + signalr.ts)
  - `b35ed33` U11 (SagaPipeline + tokens.css)
  - `fb79a79` U12 (TransitionsLog)
  - `a59188e` U4 (Outbound controllers + seed + MediatR scan)
  - `507826c` U6 (SignalR relay consumers, Outbound-only)
  - `96aecdd` U9 (Inventory SignalR wire-up + polling fallback)
  - `f1bf866` U8 (frontend Orders API + hooks + mutations)
  - `8c00555` U10 (Orders list route + sidebar rename)
  - `8b3ee6b` U13 (Orders detail route + OrderLineItems)
  - (this commit) U14 (a11y smoke extension + sign-off + CHANGELOG + README + CLAUDE + tag)

## Next implementation step

Cut a fresh branch from `v0.10.0-sprint-7-orders` and pick one of:

- **Sprint-7.5 — bundled trade-off closures**. Cosmetic SKU schema expansion (Sprint-6 trade-off #1) + camelCase wire normalisation (#6) + flash-sale dual-write to StockSync (#10) + URL-search-params persistence (#4) + reservation ledger cursor pagination (#5) + optional UNIQUE on `outbound_saga_transitions`. ~1-week point release matching Sprint-2.5 / 4.5 / 5.5 cadence.
- **Sprint-8 — Real auth + first multi-role surface**. Replace the dev-mode baked JWT with real password hashing + refresh tokens + role claims + MFA placeholder. Add picker or ops-dispatcher screen as the first multi-role surface (Sprint-7's Owner-only design has the auth perimeter; multi-role splits the UI).
- **Sprint-5.5 — Scale-gate harness closure**. Backend-only point release. Same shape as Sprint-2.5 / 4.5. Runs in parallel with any Sprint-7.5/8 frontend work.
- **Phase-3 polish**. Observability dashboards (Prometheus + Grafana for hub connect counts / saga latency / breaker state); Gateway hardening (auth middleware tightening, rate limiting on `/hub/negotiate`); portfolio README + demo video; deployment docs.
- **Public blog post derivative**. Adapted ~3000-4000 word version of `docs/methodology.md` OR a Sprint-6/7 frontend case study.

---

**Closing note**: Sprint-7 ships the methodology pattern at a *second* frontend surface and exercises the parallel-subagent-dispatch mode for the first time. The pattern held: 14 units across 5 dispatch rounds (2 inline + 3 parallel) with zero behavioural regressions caught at commit-review time. The doc-review pipeline earned its keep — 5 P1s caught before execution started — and the user-decision routing for the two architectural premise findings (hub topology + observer mechanism) shaped the saga audit-write surface in a way the literal plan would have shipped incorrectly. Frontend pattern is now a primitive: any future vertical slice can crib from the SagaPipeline + TransitionsLog + LedgerDrawer-reuse + KTD11 cell-button + KTD9 modal+drawer Esc shape without re-deciding.
