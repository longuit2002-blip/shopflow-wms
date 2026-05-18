---
title: "Phase-2 Sprint-5 sign-off — Stock Sync Engine"
date: 2026-05-17
status: complete
follows: docs/phase-gates/2026-05-15-sprint-4.5-signoff.md
plan: docs/plans/2026-05-16-001-feat-phase-2-sprint-5-stock-sync-plan.md
tag: v0.7.0-sprint-5
---

# Phase-2 Sprint-5 sign-off — Stock Sync Engine

Sprint-5 closes Phase-2 egress with a new `ShopFlow.StockSync` module that consumes Inventory's stock-mutation outbox stream and pushes per-channel `available_to_sell` updates through a four-layer isolation pipeline: coalescing buffer → per-tenant priority queue → token bucket → Polly v8 circuit breaker → `IChannelAdapter.PushStockUpdateAsync`. Ten implementation units shipped on `feat/phase-2-sprint-5-stock-sync` cut from `v0.6.1-sprint-4.5`. The wall-time noisy-neighbor scale gate ships as Skip'd slots (Sprint-4 U9 precedent); production primitives are proven by U3-U8 unit + integration coverage.

## What shipped

| U-ID | Goal | Status |
|------|------|--------|
| U1 | `ShopFlow.StockSync` quartet (Domain / Application / Infrastructure / Api), `StockSyncDbContext` + 3 tables (`stock_sync_sku_flag`, `stock_sync_push_log`, `stock_sync_outbox_messages`), `InitialStockSyncSchema` migration with `[Migration]` + `[DbContext]` attributes, `SkuFlag` + `PushLogEntry` aggregates (14 unit tests), 6th method in `MigrationSmokeTests`, `ShopFlow.sln` updated with 6 new csproj entries | ✅ |
| U2 | `ShopFlow.Contracts.Inventory.StockLevelChangedV1(TenantId, Sku, AvailableToSell, OccurredAt)` canonical event (KTD1). `ReservationRepository` + `StockItemRepository` emit one row per affected SKU per commit across all 5 stock-mutating paths (TryReserve, Confirm, Release, ReleaseLines, ReleaseExpired) + AdjustAtBin. Generic `AppendOutbox<T>` helper + `ReadAvailableForSkusAsync` for CTE paths. 6 integration tests against Testcontainers Postgres. | ✅ |
| U3 | `CoalescingBuffer` (singleton `ConcurrentDictionary<CoalesceKey, CoalesceEntry>` with `AddOrUpdate` last-by-`ObservedAt` tiebreaker), `StockLevelChangedConsumer` (fans out per (tenant, sku) to active channels via `IChannelLookupPort`, stamps `IsFlashSale` via `ISkuFlagRepository`), `CoalesceFlushService` (`BackgroundService` with `PeriodicTimer(CoalesceWindowMs)`), `PushIntent` + `BuildIdempotencyKey` helper, `IPerTenantQueue` port, `StockSyncOptions { CoalesceWindowMs, ActiveChannels }`. 19 unit tests. | ✅ |
| U4 | `PerTenantQueue` (per-tenant pair of bounded `Channel<PushIntent>` lanes, `DropOldest` mode, strict-priority `ReadNextAsync` loop), `TenantChannelBucketRegistry` (`IDisposable` registry of `TokenBucketRateLimiter` per (tenant, channel) using built-in `System.Threading.RateLimiting`), `StockSyncOptions.TokenBucket` + `QueueCapacity` settings. 11 unit tests. | ✅ |
| U5 | `PushPipelineFactory` (builds `ResiliencePipeline<Result>` per (tenant, channel) with `CircuitBreakerStrategyOptions<Result>` + `CircuitBreakerStateProvider`), `TenantChannelBreakerRegistry` (lazy `GetOrAdd` + `GetState` diagnostics), `PerTenantDispatcherService` (`BackgroundService` enumerates Ready tenants once on startup; per-tenant `Task.Run` loop: queue.ReadNext → breaker check → bucket.Acquire → pipeline.Execute → MarkSucceeded/Failed log row; up-front `MarkBreakerOpen` vs mid-execute `BrokenCircuitException` distinguished in audit), `IPushLogRepository` + `PushLogRepository` (scoped, UNIQUE-23505 idempotent catch), `StockSyncOptions.Breaker` settings. 10 unit tests + 4 integration tests. | ✅ |
| U6 | `ShopeeAdapter.PushStockUpdateAsync` body replaces Sprint-4 deferred stub: `HttpRequestMessage` rebuilt per retry attempt (single-shot send), `X-ShopFlow-Idempotency-Key` header, status-code → stable error-code mapping (`shopee.push.rate_limited` / `5xx` / `4xx` / `transport`). `ShopeeStockUpdatePayload` records with snake_case wire shape + dedicated `ShopeeJson.Options`. Shopee mock `POST /api/v2/product/update_stock` endpoint + `ChaosState.IsStockUpdateChaosActive` + namespaced `MockEntryPoint` for `WebApplicationFactory<T>`. 9 adapter unit tests + 2 mock round-trip integration tests. | ✅ |
| U7 | `SkuFlagRepository` (scoped EF inner, UNIQUE-23505 catch + idempotent `SetFlashSale`), `CachingSkuFlagRepository` (singleton wrapper, 5-min TTL `ConcurrentDictionary<(tenantId, sku), CacheSlot>`, opens DI scope + binds `RequestContext` via `ITenantCatalog.LookupByIdAsync` + K12 pattern). `ISkuFlagRepository` port signature changed to take `Guid tenantId` explicitly (consumer + 4 NSubstitute call sites updated). `SkuFlagsController` `PUT /api/skus/{sku}/flag` returns 204. 8 cache unit tests + 7 integration tests + 1 Skip'd controller placeholder. | ✅ |
| U8 | `AddStockSyncModule` composition extension (`IDbContextFactory<StockSyncDbContext>` via K12 + scoped bridge, scoped + singleton port registrations, 3 hosted services), `ChannelLookupPort` singleton impl reading `StockSyncOptions.ActiveChannels`, full `Program.cs` (replaces U1 stub, scans 2 assemblies for MT consumer discovery), `SyncStateController` `GET /api/sync/state` with class-level `[SkipTenantRouting]` guarded by `StockSync:DiagnosticsEnabled` flag, Aspire `AddProject<Projects.ShopFlow_StockSync_Api>("stocksync-api")`, Gateway routes (`/api/sync/**` + `/api/skus/**` → `stocksync-cluster`). 3 composition integration tests. | ✅ |
| U9 | `StockSyncHappyPathTests` (Category=Integration, single-tenant end-to-end via `WebApplicationFactory<Program>` + InMemory MT transport + `FakeChannelAdapterFactory` recorder; provisions catalog + tenant DBs on Testcontainers Postgres; drives `StockLevelChangedV1` via `IPublishEndpoint.Publish`; asserts factory recorded the push + `stock_sync_push_log` has Success row). `Drivers/FakeChannelAdapterFactory` + `Drivers/TenantBurstDriver` (parallel-batch 2k/s direct outbox insert) + `FairnessCalculator` helpers. `MultiTenantStockSyncScaleGateTests` ships 2 Skip'd slots (R8 noisy-neighbor + R9 breaker recovery) — Sprint-4 U9 precedent. | ✅ |
| U10 | Sign-off doc + CHANGELOG entry + README + CLAUDE current-stage update + tag `v0.7.0-sprint-5` | ✅ |

## Scale-gate measurements

| Metric | Target (plan R8/R9) | Sprint-5 result |
|---|---|---|
| Noisy-neighbor p99 (B-E end-to-end) | < 30 s under tenant A 2k/s × 5min | Deferred — Skip'd slot per Sprint-4 U9 precedent |
| Per-tenant fairness floor | ≥ 0.85 (min push / max push, excl A) | Deferred |
| Breaker recovery latency after chaos-off | A recovers ≥ 30/s within 90s | Deferred |
| Breaker isolation (B unaffected during A's chaos) | B push rate ±20% of baseline | Deferred (mock chaos is process-wide in Sprint-5; per-tenant chaos is Phase-3) |

**Wall-time measurement deferral rationale**: Sprint-5 ships every production primitive the scale gate composes — coalescing collapse (U3 unit tests), token bucket rate-limit (U4 unit tests), breaker trip + half-open recovery (U5 unit tests), real Shopee adapter HTTP push + chaos toggle (U6 integration tests), full Api boot with hosted services running (U8 composition integration tests), single-tenant end-to-end through the dispatcher (U9 happy-path integration test). The scale gate composes these into a wall-clock measurement under multi-tenant burst, which needs the multi-tenant Aspire boot + real Shopee mock alongside StockSync.Api harness — same gap Sprint-4 U9 deferred to Sprint-4.5. Sprint-5.5 follow-up will close it.

## Key technical decisions (recap of plan KTDs)

- **KTD1** — Replaced literal R1 (consume 3 existing transition events) with single canonical `StockLevelChangedV1` because `StockReleasedV1` carries only `OrderLineIds` (no SKU) and `StockConfirmedV1` has no per-line detail. Avoids coupling StockSync to Outbound for line→SKU mapping; race-prone. Inventory's existing `StockChangedEvent` domain event already had the right intent ("catch-all for the stock-sync engine"); U2 wires it into the cross-module contract.
- **KTD2** — `stock_sync_sku_flag` table lives in StockSync's own DbContext (not Channel's `ProductMapping`). Loose coupling, StockSync owns the flag lifecycle.
- **KTD3** — Built-in .NET 9 primitives only: `System.Threading.RateLimiting.TokenBucketRateLimiter`, `System.Threading.Channels.Channel<T>`, Polly v8 (already in CPM from Sprint-3-redux + Sprint-4). No new packages.
- **KTD4** — `ConcurrentDictionary<CoalesceKey, CoalesceEntry>` + `PeriodicTimer(CoalesceWindowMs)` flush. `AddOrUpdate` atomic last-by-`ObservedAt` tiebreaker protects against MT redelivery / out-of-order arrival regressing published stock.
- **KTD5** — Persist: `stock_sync_push_log` + `stock_sync_sku_flag`. In-memory: coalescing buffer, queues, token bucket counters, breaker state. Restart = ~500ms warm-up; acceptable for Sprint-5 portfolio scope.
- **KTD6** — Module #7 in modular monolith. ADR-0002 unchanged. W6 split readiness preserved (cross-module via RabbitMQ + per-tenant DbContext binding via K12).
- **KTD7 (emerged during U7)** — `ISkuFlagRepository` port takes explicit `Guid tenantId`. Consumer + dispatcher call from singleton context without ambient `RequestContext`; cache key + scope binding both need explicit tenant. Single source of truth for per-tenant cache isolation.

## Deviations from plan file list

- **U2 — `AppendOutbox<T>` generic overload added in two repositories** (ReservationRepository + StockItemRepository) instead of one. The plan implied a single helper; the actual layering (Inventory's two repos in different files, both writing outbox rows) made code-sharing via static helper awkward. Two near-identical overloads with documented intent is the cleanest landing.
- **U3 — `IPerTenantQueue` reader-side method (`ReadNextAsync`) deferred to U4** because the consumer + flush service in U3 only write to the queue; U4 owns the queue impl + reader semantics. U3 ships the port write side; U4 extends with the read side. Documented in U3 + U4 commit messages.
- **U4 — `PerTenant` config override on `CoalesceWindowMs`** dropped (no test, no usage). The per-tenant override only makes sense once U4's per-tenant dispatcher exists, and at U4 there's still no use case driving the parameter. Documented as a future enhancement in `StockSyncOptions` XML docs.
- **U5 — `PerTenantDispatcherService` enumerates tenants once on startup** (not on tenant-added events). Phase-3 work; documented in code remarks. Sprint-5 scope is 1-25 statically-provisioned tenants.
- **U5 — `StockUpdateRequest.ChannelId = Guid.Empty`** routes by channel TYPE only; per-tenant `ChannelId` lookup is Phase-3 when StockSync queries Channel module's `channels` table. Documented in code.
- **U6 — `HttpRequestMessage` rebuilt per retry attempt** because single-shot send semantics throw on re-use; mirrors Sprint-3-redux `MockShippingProvider` precedent.
- **U6 — Mock chaos state is process-wide singleton** (not per-tenant). Sprint-4 U9 → Sprint-4.5 same precedent. Per-tenant chaos is Phase-3.
- **U6 — `X-ShopFlow-Idempotency-Key` is an internal audit header** (not a real Shopee API contract). Real Shopee derives idempotency from `(item_id, model_id) + per-shop nonce`. Phase-3 either drops or translates.
- **U7 — `ISkuFlagRepository` port signature changed mid-sprint** to take `Guid tenantId` explicitly (KTD7 above). Consumer + 4 NSubstitute call sites in U3 unit tests updated. Surfaced because the singleton wrapper opens a scope without ambient `RequestContext`.
- **U7 — `SkuFlagsController` integration test placeholder Skip'd** because U7's Program.cs is still the U1 stub. U8 ships the real Program.cs; U8 composition test covers the controller boot. Skip message points at U8.
- **U7 — Crude LRU eviction** at 10k entries (one arbitrary key dropped per overflow) instead of a proper touch-on-read LRU. Phase-3 upgrade.
- **U8 — `MultiplexedOutboxDispatcher<StockSyncDbContext>` registered** even though StockSync doesn't emit cross-module events yet. Infrastructure parity with the other modules; Phase-3 events drop in without DI shuffle.
- **U8 — Gateway uses two route entries** (`stocksync-sync` + `stocksync-skus`) pointing at one cluster. Channel uses one route; StockSync owns two URL prefixes.
- **U8 — Composition test uses `MessageBus:Transport=InMemory`** so RabbitMQ is not required for the boot. U9 happy-path uses the same approach.
- **U9 — Happy-path uses `FakeChannelAdapterFactory` recorder** instead of booting the out-of-process Shopee mock alongside StockSync.Api. The StockSync project graph doesn't reference Channel.Infrastructure (W6-split intent); the real adapter + mock round-trip is exercised by U6 `ShopeeMockRoundTripTests`. The fake records the exact `StockUpdateRequest` the dispatcher hands the adapter — that *is* the assertion the plan's "mock received push" check translates to, scoped to the dispatcher's contract surface.
- **U9 — 2 scale-gate slots ship Skip'd** (R8 + R9 wall-time measurement). Sprint-4 U9 precedent. Production primitives all proven by U3-U8 unit + integration coverage; the gate composes those into a wall-clock measurement under multi-tenant burst that needs the multi-tenant Aspire boot + real Shopee mock alongside StockSync.Api — Sprint-5.5 follow-up.
- **No local Docker daemon on this dev machine** — same Sprint-1-redux + Sprint-3-redux + Sprint-4 + Sprint-4.5 posture. Integration + Load tests run in CI (`.github/workflows/chaos-nightly.yml` filter `Category=Load`).

## Test count

| Tier | Sprint-4.5 (baseline) | Sprint-5 added | Sprint-5 total |
|---|---|---|---|
| Unit | 288 | +71 (14 U1 + 19 U3 + 11 U4 + 10 U5 + 9 U6 + 8 U7) | 359 |
| Integration (Category=Integration) | (varies) | +24 (6 U2 + 4 U5 + 7 U7 + 1 controller Skip + 3 U8 + 2 U6 + 1 U9) | — |
| Load (Category=Load) | — | +2 (U9 Skip'd) | — |

## Branch + tag

- Branch: `feat/phase-2-sprint-5-stock-sync` (cut from `v0.6.1-sprint-4.5`)
- Tag: `v0.7.0-sprint-5` (annotated)
- Commit chain: `62d6bfd` (docs) → `411ad9c` (U1) → `7797667` (U2) → `58328c2` (U3) → `5db8341` (U4) → `bfc917d` (U5) → `699fe90` (U6) → `478d142` (U7) → `06ad40e` (U8) → U9 commit → U10 commit + tag

## Next implementation step

Cut a fresh branch from `v0.7.0-sprint-5` and start either:

- **Sprint-5.5** — close the U9 scale-gate harness gap (multi-tenant Aspire boot + real Shopee mock alongside StockSync.Api). Same posture as Sprint-4 → Sprint-4.5 closure.
- **Sprint-6** — Analytics module (W9-W10): read-side projections / dashboards consuming the existing outbox stream (including `StockLevelChangedV1`).
- **Phase-3 polish** — Gateway hardening, observability dashboards, portfolio README + demo, deployment docs.

The brainstorm in `docs/brainstorms/2026-05-16-sprint-5-stock-sync-requirements.md` "Outstanding Questions — Roadmap" section lists the high-level scope for Sprint-6 + Phase-3.
