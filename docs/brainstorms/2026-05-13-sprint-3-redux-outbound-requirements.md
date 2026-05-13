---
date: 2026-05-13
topic: sprint-3-redux-outbound-saga
---

# Sprint-3-redux — Outbound module + fulfillment saga + mocked carrier

## Summary

Sprint-3-redux ships the Outbound module: a manually-created Order aggregate, a MassTransit state-machine fulfillment saga (Created → Reserved → Picked → Packed → Shipped, with pick-failure compensation to Cancelled), a per-tenant bounded `Channel<T>` pick-wave generator that batches orders by 15-min window + shipping_profile, a status-flip pack endpoint with a weight check, and a ship endpoint that simulates a mocked carrier call (1-3 s delay, generated label URL, simulated tracking pushback). Cross-module flow with Inventory uses command-response contracts via MassTransit — Inventory gains three new consumers wrapping Sprint-1-redux's `ReservationRepository.{TryReserveAsync, ConfirmAsync, ReleaseAsync}`. Stock-deduction on ship confirmation flows back to Inventory inside the saga's Shipped transition.

---

## Problem Frame

Sprint-1-redux ships the reservation ledger but nothing reserves against it from a real order. Sprint-2-redux ships the Inbound module so stock physically lands in bins, but again no order flow consumes it. The system today can reserve stock if asked, and accept stock when received, but cannot answer the question: "what happens to one order from arrival to ship?" Until the Outbound module + fulfillment saga land, the reservation ledger and the bin tracking are correct primitives without an end-to-end customer outcome.

Architecturally, this is also the first cross-module flow in **both directions**. Sprint-2-redux's Inbound → Inventory flow was one-way (Inbound emits, Inventory consumes). The fulfillment saga must reserve stock (Outbound → Inventory), wait for confirmation (Inventory → Outbound), confirm on ship (Outbound → Inventory), and compensate on pick failure (Outbound → Inventory). That's the canonical request/response cross-module pattern the rest of the 12-week roadmap will inherit — the saga, the pick-wave Channel pipeline, and the cross-module contract style all become reference shapes for Phase-2 channel adapters and Phase-3 analytics.

A third pain: the v3.0 plan calls for a 2,000 orders/tenant × 3 tenants in 1 minute scale gate, all reaching packed state within 5 min p99. Until Sprint-3-redux lands the Channel-based pick-wave pipeline, there is no measurable end-to-end throughput. Sprint-1-redux's W3 scale gate measured reservation correctness under contention; this one measures end-to-end fulfillment latency under load.

---

## Actors

- A1. **Warehouse Operator**: Triggers each saga manual transition (pick-confirm, pick-fail, pack-confirm, ship-confirm) through HTTP endpoints. Sprint-3-redux exposes the surface; UI lands in Phase-3 Sprint-7.
- A2. **Outbound Module API**: Owns the Order aggregate + saga orchestration entry point. Receives manual order creation + state-transition POSTs from operators or load-test driver.
- A3. **Fulfillment Saga**: MassTransit state-machine instance per order, persists in the Outbound tenant DB's `saga_state` table. Publishes commands to Inventory, listens for result events, transitions on each event.
- A4. **Pick Wave Generator**: Hosted background service per tenant that drains the in-process pick-request Channel<T> and emits PickWave aggregates with assigned pickers (round-robin in Sprint-3-redux).
- A5. **Inventory Module Cross-Module Consumers**: Three new consumers (`ReserveStockConsumer`, `ConfirmStockConsumer`, `ReleaseStockConsumer`) wrap Sprint-1-redux's existing `ReservationRepository` methods, emit result events via Inventory's outbox.
- A6. **Mocked Carrier Provider**: `IMockShippingProvider` simulates label generation with a 1-3 s delay, returns label URL + tracking number, occasionally fails to exercise the Polly retry path.
- A7. **Load-Test Order Generator**: Test-suite-only class that emits 2,000 orders per tenant within 1 min for the U8 scale gate. Not a production code path.

---

## Key Flows

- F1. **Happy-path fulfillment** (Created → Shipped)
  - **Trigger:** Operator (or load-test driver) POSTs an order with `(channel_external_order_id, shipping_profile, lines[])`.
  - **Actors:** A1/A2/A3/A4/A5/A6.
  - **Steps:**
    1. Outbound API persists order + lines in Outbound tenant DB, idempotent on `UNIQUE(channel_external_order_id)`. Saga starts in `Created`.
    2. Saga transitions to `AwaitingReservation` and publishes `ReserveStockV1` command (one per line, or aggregate per order — TBD planning) to MassTransit.
    3. Inventory's `ReserveStockConsumer` consumes, calls `ReservationRepository.TryReserveAsync`, emits `StockReservedV1` (success) OR `StockReservationFailedV1` (oversold) per line.
    4. Saga consumes the result. All-success → state `Reserved` + writes `PickRequestV1` to the tenant's in-process Channel + transitions to `AwaitingPick`. Any-failure → state `Cancelled` + (already-reserved lines compensated via `ReleaseStockV1`).
    5. `PickWaveGeneratorService` drains the Channel; within 15 min of first eligible order arriving (or sooner if shipping_profile group reaches max-wave-size) emits a `PickWave` with all eligible orders + round-robin picker assignment.
    6. Operator POSTs `confirm-pick` → saga transitions `AwaitingPick → Picked → AwaitingPack`.
    7. Operator POSTs `confirm-pack` with actual weight → saga transitions `Picked → Packed → AwaitingShip`. Weight ±10% mismatch surfaces a non-blocking warning in the response payload.
    8. Operator POSTs `confirm-ship` → saga publishes `ConfirmStockV1` to Inventory + triggers `IMockShippingProvider.CreateLabelAsync` (1-3 s delay, Polly retry on transient fail). On success, label_url + tracking_number stored on Order; saga transitions to `Shipped`. `TrackingPushedV1` event published to a stub `ChannelTrackingConsumer` (logged, no-op until Phase-2 Sprint-4).
    9. Inventory's `ConfirmStockConsumer` consumes `ConfirmStockV1`, calls `ReservationRepository.ConfirmAsync` (stock_items.reserved decreases, reservation transitions to Confirmed), emits `StockConfirmedV1`.
  - **Outcome:** Order in Shipped state, label_url + tracking populated, reservation in Inventory at Confirmed state, stock physically reduced.
  - **Covered by:** R1, R2, R4, R5, R6, R8, R9, R10, R11, R12, R13.

- F2. **Pick-failure compensation** (Reserved → Cancelled)
  - **Trigger:** Operator finds the line cannot be picked (physical miss, damage) and POSTs `mark-pick-failed` after the saga has already reached `AwaitingPick`.
  - **Actors:** A1, A2, A3, A5.
  - **Steps:**
    1. Operator POSTs the mark-pick-failed endpoint with a reason string.
    2. Saga transitions `AwaitingPick → CompensatingReservation` and publishes `ReleaseStockV1` per reserved line.
    3. Inventory's `ReleaseStockConsumer` calls `ReservationRepository.ReleaseAsync`, emits `StockReleasedV1`.
    4. Saga consumes each `StockReleasedV1`; once all lines released, transitions to `Cancelled` terminal state.
    5. Order status = Cancelled, reason recorded, reservation rows in Inventory at Released state.
  - **Outcome:** Reservation rolled back atomically within 60 s p99 per the scale gate target.
  - **Covered by:** R7, R9, R11, R12.

- F3. **Pick-wave generation** (in-process Channel pipeline)
  - **Trigger:** Saga writes a `PickRequestV1` envelope to the tenant's in-process Channel<PickRequest>.
  - **Actors:** A3, A4.
  - **Steps:**
    1. `IPickQueue` resolves the tenant's Channel<PickRequest> (one per tenant id, created on-demand, gated by `BoundedChannelOptions(capacity: 1000)`).
    2. `PickWaveGeneratorService` (hosted service) drains the Channel in a loop, accumulating pick-requests into a sliding 15-min window indexed by `(tenant_id, shipping_profile)`.
    3. Window closes when 15 min elapses since first arrival OR the group reaches `max_wave_size` (50 in Sprint-3-redux; configurable later).
    4. On close: emit a `PickWave` aggregate with order_ids + assigned picker (round-robin from a tenant-scoped picker pool seeded by test fixtures), update each order's `pick_wave_id`, publish a `PickWaveAssignedV1` event for downstream observability.
  - **Outcome:** Order's `pick_wave_id` populated; operator can subsequently confirm-pick.
  - **Covered by:** R3, R5.

- F4. **Mocked carrier ship + tracking pushback**
  - **Trigger:** Operator POSTs `confirm-ship`.
  - **Actors:** A2, A6.
  - **Steps:**
    1. Outbound API resolves `IMockShippingProvider` and calls `CreateLabelAsync(order)`.
    2. Impl waits 1-3 s (random), with a 5% transient-fail probability that the Polly retry handler covers (3 retries with 200 ms backoff).
    3. On success returns `(LabelUrl, TrackingNumber)`. Outbound writes both onto the order; saga transitions to Shipped.
    4. Outbound's outbox emits `TrackingPushedV1` event (carries order_id, tracking_number, channel_id placeholder).
    5. Stub `ChannelTrackingConsumer` (in Outbound for now; moves to Channel module Phase-2) consumes + logs.
  - **Outcome:** Order has label + tracking; tracking event is observable.
  - **Failure path:** All 3 Polly retries exhausted → carrier-call returns failure; ship endpoint responds 503 ProblemDetails; saga stays in AwaitingShip (operator can retry); no stock confirmation triggered.
  - **Covered by:** R10, R13.

---

## Requirements

**Order lifecycle and persistence**
- R1. Order has an idempotency anchor `UNIQUE(channel_external_order_id)` per tenant DB. Manual orders auto-generate the id; load-test generator uses deterministic ids.
- R2. Order schema lives in Outbound's tenant DB: `orders` (id PK, channel_external_order_id UNIQUE, shipping_profile, status, expected_weight_total, actual_weight_total nullable, label_url nullable, tracking_number nullable, pick_wave_id nullable FK, created_at, updated_at) plus `order_lines` (id PK, order_id FK, sku, qty, expected_weight nullable), `pick_waves`, `pick_assignments`, `saga_state` (MassTransit), `outbound_outbox_messages` (Sprint-2.5 per-module prefix). No `tenant_id` on any business table per ADR-0003.
- R3. Order status mirrors saga state with a one-to-one mapping. The order row's status updates atomically with each saga transition.

**Saga state machine**
- R4. Saga states: `Created → AwaitingReservation → Reserved → AwaitingPick → Picked → AwaitingPack → Packed → AwaitingShip → Shipped` (terminal) / `CompensatingReservation → Cancelled` (terminal). All wait-states explicit so the saga always has a definite state per AGENTS.md §6.44. Saga persistence uses MassTransit's EF Core saga repository against `saga_state`.
- R5. Saga is correlated by `order_id` (the Order aggregate's primary key, NOT the channel_external_order_id). MassTransit's saga repository's pessimistic concurrency mode handles concurrent state-transition messages on the same order.

**Cross-module reservation contracts**
- R6. Cross-module contracts live in `ShopFlow.Contracts.Inventory.*` (peer to `Inbound.*` from Sprint-2-redux). Sprint-3-redux ships:
  - Commands: `ReserveStockV1`, `ConfirmStockV1`, `ReleaseStockV1`
  - Result events: `StockReservedV1` (success), `StockReservationFailedV1` (oversold), `StockConfirmedV1`, `StockReleasedV1`
  - All carry the standard envelope: `tenant_id`, `correlation_id`, `occurred_at` UTC.
- R7. Three new Inventory consumers (`ReserveStockConsumer`, `ConfirmStockConsumer`, `ReleaseStockConsumer`) wrap the Sprint-1-redux `ReservationRepository` methods and emit result events through Inventory's outbox (`inventory_outbox_messages`). Consumer idempotency is delegated to the underlying repository methods, which already use `UNIQUE(order_id)` for reservations and state-machine guards for confirm/release.
- R8. The saga listens for the result events and transitions accordingly. A `StockReservationFailedV1` for any line triggers the compensation path for already-reserved lines on the same order.

**Pick wave pipeline**
- R9. One bounded `Channel<PickRequestV1>` per tenant via `IPickQueue.GetWriter(tenantId)` / `GetReader(tenantId)`. Capacity 1000 with `BoundedChannelFullMode.Wait` (back-pressures the saga when the channel is full).
- R10. `PickWaveGeneratorService` is a hosted background service that drains every tenant's channel in a loop with 15-min sliding-window batching grouped by `(tenant_id, shipping_profile)`. Window closes on time elapse OR `max_wave_size=50` orders. On close: PickWave row written + picker assigned round-robin + each order's `pick_wave_id` updated + `PickWaveAssignedV1` event published.

**Operator endpoints**
- R11. Six Outbound endpoints:
  - `POST /api/outbound/orders` (manual create)
  - `GET /api/outbound/orders/{id}` (read-back with saga state)
  - `POST /api/outbound/orders/{id}/confirm-pick` (saga: AwaitingPick → Picked → AwaitingPack)
  - `POST /api/outbound/orders/{id}/mark-pick-failed` body `{reason}` (saga: AwaitingPick → CompensatingReservation; F2)
  - `POST /api/outbound/orders/{id}/confirm-pack` body `{actual_weight}` (saga: AwaitingPack → Packed → AwaitingShip; weight-mismatch surfaces non-blocking warning)
  - `POST /api/outbound/orders/{id}/confirm-ship` (triggers mocked carrier call + saga Shipped + cross-module Confirm)

**Mocked shipping carrier**
- R12. `IMockShippingProvider.CreateLabelAsync(Order)` returns `(LabelUrl, TrackingNumber)`. Impl delays 1-3 s random, 5% transient-fail rate, Polly handles 3 retries with 200 ms backoff. On final failure surface 503 ProblemDetails; saga stays in AwaitingShip; operator retries.
- R13. Post-ship `TrackingPushedV1` event published to a stub `ChannelTrackingConsumer` (Outbound's namespace for now; Phase-2 Sprint-4 channel adapter moves it). Carries `(order_id, tracking_number, channel_id placeholder, occurred_at)`.

**Cross-module stock confirmation on ship**
- R14. `confirm-ship` endpoint publishes `ConfirmStockV1` per line via the Outbound outbox in the same transaction as the saga's Shipped transition. Inventory's `ConfirmStockConsumer` runs `ReservationRepository.ConfirmAsync` (decrements stock_items.reserved; reservation → Confirmed) and emits `StockConfirmedV1`. Sprint-3-redux assumes ConfirmAsync never fails after Reserve succeeded; if it does (e.g., concurrent admin reset), the failure surfaces as a manual reconciliation task in operator logs — Phase-2 builds the reconciliation flow.

**Tests and gates**
- R15. Unit tests cover the saga state machine in isolation (MassTransit's `InMemoryTestHarness` style) + Order aggregate state transitions + PickWave aggregate.
- R16. Integration tests use Testcontainers Postgres + the existing in-memory MassTransit pattern from Sprint-2-redux's `InboundConfirmedConsumerTests`. Coverage: happy-path saga end-to-end, pick-failure compensation, pick-wave batching by window + profile, mocked-carrier success + retry-exhaust, manual idempotency on duplicate order POST.
- R17. **Scale gate (the headline test, `Category=Load`)**: 2,000 orders/tenant × 3 tenants ingested in 1 min via `LoadTestOrderGenerator`. Assertions per Product Plan §9.3:
  - All orders reach Packed state within 5 min p99 per tenant (happy-path variant; ship triggered automatically after pack via test driver)
  - 5% pick-failure injection variant: all failed sagas reach Cancelled state with reservations released within 60 s p99 per tenant
  - Fairness floor across tenants: min(p99) / max(p99) ≥ 0.85 (carry-forward W3 Sprint-1-redux discipline)
- R18. The cross-module reservation flow integration test (Outbound → Inventory → Outbound) lands as part of U9 — uses both modules' migrations against a single Testcontainers Postgres (the Sprint-2.5 outbox-rename made this possible).

---

## Acceptance Examples

- AE1. **Covers R1, R3.** Given a manual order POST with `channel_external_order_id="ORDER-EXT-1"`, when the same id is POSTed a second time, the second call returns 200 with the SAME order_id (idempotent), and the orders table has exactly 1 row.
- AE2. **Covers R4, R5, R6, R8.** Given an order in AwaitingReservation state with two lines (qty=10, qty=5) against a tenant with stock_items.available=50 for both SKUs, when both ReserveStockV1 commands are published and Inventory emits two StockReservedV1 result events, the saga transitions to Reserved and writes a PickRequest envelope to the tenant's pick channel.
- AE3. **Covers R8.** Given an order with two lines where line 1 reserves successfully but line 2 fails with StockReservationFailedV1, the saga publishes ReleaseStockV1 for line 1, waits for StockReleasedV1, then transitions to Cancelled. Final state: order.status=Cancelled, reservation for line 1 in Inventory at Released state, no reservation row for line 2.
- AE4. **Covers R9, R10.** Given a tenant with 50 orders eligible for picking within a 15-min window — 30 with shipping_profile="standard", 20 with "express" — the PickWaveGeneratorService produces 2 pick waves: one with the 30 standard orders, one with the 20 express orders. Each wave has an assigned picker via round-robin.
- AE5. **Covers R11, R14.** Given an order in AwaitingShip state, when the operator POSTs confirm-ship and the mocked carrier returns successfully on first try, the saga publishes ConfirmStockV1, transitions to Shipped, the order row has `label_url` + `tracking_number` populated, and Inventory's stock_items.reserved decreases by the order's total quantity.
- AE6. **Covers R12.** Given the mocked carrier configured to fail twice before succeeding, when confirm-ship is called, the Polly retry executes the carrier 3 times (1 initial + 2 retries), then the third attempt succeeds; saga transitions to Shipped. Wall time is at least 2×200 ms back-off + 3×(1-3 s delay) ≈ 4-10 s.
- AE7. **Covers R12.** Given the mocked carrier configured to fail every time, when confirm-ship is called, the API responds 503 ProblemDetails after 4 attempts, the saga remains in AwaitingShip, no ConfirmStockV1 was published, and a subsequent confirm-ship retry succeeds when the carrier is reset.

---

## Success Criteria

- A warehouse operator (or load-test driver) can drive a single order through the full happy-path saga via HTTP calls — POST order, watch reservation succeed, see pick-wave assignment, confirm-pick, confirm-pack with weight check, confirm-ship — and observe the resulting Inventory stock change without manual SQL.
- The pick-failure compensation flow correctly rolls back Inventory state. After a pick-failed event, the reservation rows in Inventory's tenant DB return to Released status and the order is Cancelled. The cross-tenant routing remains correct throughout.
- The MassTransit saga is the *single* source of truth for an order's fulfillment state; no in-process state machine in the application code shadows it. Saga state is queryable via the Order aggregate's `status` column which mirrors saga state.
- The W5 scale gate runs end-to-end and produces measured p99 numbers per tenant + fairness floor. On dev hardware the throughput targets are aspirational (carry-forward Sprint-1-redux hardware caveat); on production-shape Linux CI hardware the targets are real gates.
- A downstream developer reading the requirements doc + Tech Design §10-12 can implement Sprint-3-redux without inventing saga states, contract payloads, pick-wave batching rules, or carrier-mock behavior.
- Sprint-3-redux closes Phase-1's customer-funnel triangle: Inventory (Sprint-1-redux) holds stock, Inbound (Sprint-2-redux) fills it, Outbound (Sprint-3-redux) drains it. The system can operate a single warehouse end-to-end without channel integration — Phase-2 starts.

---

## Scope Boundaries

- **Mock channel webhook ingestion of orders** → Phase-2 Sprint-4 with the channel adapter framework. Sprint-3-redux ships manual API + load-test generator only.
- **Customer-initiated order cancel** → Phase-2 (no customer-facing surface in Phase-1 anyway).
- **Saga timeout-based compensation** (e.g., reservation auto-released if saga doesn't progress within X hours) → Phase-2; requires MassTransit scheduler wiring.
- **Zone-aware pick optimization within a wave** → Phase-3+ advanced slotting.
- **Smart picker assignment** (skill matching, workload balancing) → round-robin in Sprint-3-redux; smarter assignment Phase-3+.
- **Real carrier API integration** (Shopee/Lazada/GHN label generation) → Phase-2 Sprint-4.
- **Multi-line partial fulfillment** (operator marks 1 of 3 lines failed but ships the other 2 lines) → Sprint-3-redux treats pick failure as whole-order. Line-level partial fulfillment Phase-2+.
- **Tenant-configurable pick-wave window** (Product Plan §3.3 mentions "configurable per-tenant") → Sprint-3-redux hardcodes 15 min. Tenant override Phase-2.
- **Channel<T> backpressure tuning under load** → use `BoundedChannelOptions(capacity: 1000, FullMode=Wait)`. Tuning belongs in scale-gate diagnostics if the limit binds.
- **SignalR push of saga state changes to ops dashboard** → Phase-3 Sprint-7.
- **Analytics views over order or saga state** → Phase-3 Sprint-8.
- **Saga rehydration / replay tooling** → assumed not needed at MVP scale (MassTransit's saga repository handles redelivery natively).

### Deferred to Follow-Up Work

- **Stock-confirmation failure reconciliation**: Sprint-3-redux assumes `ReservationRepository.ConfirmAsync` never fails after `TryReserveAsync` succeeded (the reservation was already validated). In production, concurrent admin adjustments could theoretically race the saga's confirm — surface that as a manual reconciliation log entry; Phase-2 builds the resolution workflow.
- **CSharpier formatting cleanup** carries forward from prior sprints — one consolidating commit lands when Husky pre-commit is installed.
- **Per-tenant carrier configuration** (different mock provider per tier / region) — single global mock for Sprint-3-redux; per-tenant config Phase-2.

---

## Key Decisions

- **Pick wave batching by 15-min window + shipping_profile.** Matches Product Plan §3.3. Yields larger picker batches, fewer picker context-switches, and surfaces a real Channel-based pipeline shape the rest of the system can reuse. Per-order-no-batching was rejected because it would defer the entire Channel pipeline + miss the realistic operator efficiency story.
- **MassTransit state-machine saga, not in-process state machine.** AGENTS.md §6.44 mandates MassTransit; risk row already flagged the learning curve. The fallback (in-process state machine within Outbound's domain model) stays available if MassTransit's behavior under saga proves opaque — captured as a Phase-1 risk mitigation per Product Plan §10.
- **Pick-failure-only compensation.** Aligns with Product Plan §9.3 scale gate ("inject 5% pick failures"). Customer-cancel and saga-timeout are deferred — there's no customer-facing surface in Phase-1, and timeout-based compensation needs MassTransit's scheduler which adds complexity for marginal value at this stage.
- **Mocked carrier with 1-3 s delay + 5% retry-trigger + label URL + tracking pushback to stub consumer.** Closer to production shape than pure status-flip; exercises Polly retry path; mirrors the kind of work the real channel adapters will do in Phase-2 without needing actual marketplace APIs. Tracking pushback stub consumer lives in Outbound's namespace for now; Phase-2 Sprint-4 channel adapter moves it.
- **Manual API + LoadTestOrderGenerator, NO mock-channel webhook ingestion.** Manual API covers the operator persona; load-test generator covers the scale gate; webhook ingestion is the canonical entry point for marketplace orders and earns the full Sprint-4 effort with persistence + retry + per-channel idempotency.
- **Command-response via MassTransit for cross-module reservation, NOT direct in-process call.** AGENTS.md §10 forbids cross-module DbContext access. Six new contracts (3 commands + 3 events) is the cost; the upside is the W6 mechanical split is mechanical — no re-architecting the saga when modules move to separate processes.
- **Saga correlation by `order_id` (Outbound's PK), not `channel_external_order_id`.** Decouples saga identity from the external idempotency anchor. If a marketplace re-sends an order id with different content (rare but possible), the saga still has a stable internal identifier.
- **Cross-module contracts namespaced `ShopFlow.Contracts.Inventory.*`.** Peer to `ShopFlow.Contracts.Inbound.*` from Sprint-2-redux. Module-named subnamespace makes it obvious which module owns the contract's wire format.

---

## Dependencies / Assumptions

- Foundation tag `v0.4.1-sprint-2.5` (post Sprint-2.5 outbox rename + OutboxJsonOptions consolidation). Branch `feat/phase-1-sprint-3-redux-outbound` is already cut.
- Sprint-1-redux's `ReservationRepository.{TryReserveAsync, ConfirmAsync, ReleaseAsync}` are stable. Sprint-3-redux's Inventory consumers wrap them directly.
- Sprint-2-redux's `MultiplexedOutboxDispatcher<TContext>` pattern is reused: Outbound gets its own dispatcher instance against `OutboundDbContext`; Inventory's existing dispatcher already drains `inventory_outbox_messages`.
- `MassTransit.EntityFrameworkCore` package for saga persistence (in addition to MassTransit + MassTransit.RabbitMQ already in CPM). Add to Directory.Packages.props.
- `Polly` package for the mocked-carrier retry. Likely transitively present; verify in planning.
- AGENTS.md §10 (cross-module via MassTransit) + §6.44 (MassTransit saga) + §6.42 (envelope must carry tenant_id, correlation_id, occurred_at) are all in force.
- The Aspire AppHost RabbitMQ container is load-bearing for `task up`; Sprint-3-redux integration tests still run in-memory MassTransit (Testcontainers RabbitMQ comes when the cross-module flow test for the saga exercises actual broker semantics — likely a Sprint-3.5 hardening unit).
- The Phase-2 reconciliation flow is committed in the 12-week roadmap; Sprint-3-redux's "ConfirmAsync never fails" assumption is acceptable because Phase-2 catches the edge case.

---

## Outstanding Questions

### Resolve Before Planning

(None — Phase 2.5 confirmed scope.)

### Deferred to Planning

- [Affects R6][Technical] Concrete contract field shapes for the 7 new contracts (e.g., is `ReserveStockV1` one command per line or per order with a `LineReservation[]` array?). Planning resolves against the saga's natural correlation shape.
- [Affects R5][Technical] Whether MassTransit's saga `CorrelateById` works directly on `order_id` as `Guid` or whether a wrapper `CorrelationId` field is needed. Verify against MassTransit 8.3.4 docs.
- [Affects R9][Technical] Exact `IPickQueue` shape — `IDictionary<Guid, Channel<PickRequestV1>>` keyed by tenant id, or `ConcurrentDictionary`-wrapped factory? Planning picks the cleanest seam.
- [Affects R10][Technical] How the 15-min sliding-window batching is implemented in the hosted service — `PeriodicTimer` + per-window dictionary, or `ChannelReader.ReadAllAsync` with timestamp comparison. Both work; planning picks based on test ergonomics.
- [Affects R12][Needs research] Polly v8 vs v7 API surface. Polly v8 changed the retry pipeline shape significantly — verify which version is pinned and write the mock against the right API.
- [Affects R17][Technical] Load-test generator concurrency shape — `Task.WhenAll` with 100 parallel POSTs, or NBomber-style? Sprint-1-redux used `Task.WhenAll`; carry that pattern unless a reason emerges.
- [Affects R7][Technical] Confirm/Release consumer transaction shape — both calls already commit their own EF transactions; do we wrap them in a `TransactionScope` to ensure the result-event publication is atomic with the state change, or is the existing `_db.SaveChangesAsync` boundary sufficient? Planning resolves against Sprint-1-redux's existing pattern.
- [Affects R13][Technical] `TrackingPushedV1` consumer location — Outbound module today, Channel module Phase-2. Does the contract namespace stay `ShopFlow.Contracts.Outbound.*` (event origin) or move to `ShopFlow.Contracts.Channel.*` (consumer destination)? Planning picks based on AGENTS.md §10 contracts-by-event-origin convention.
