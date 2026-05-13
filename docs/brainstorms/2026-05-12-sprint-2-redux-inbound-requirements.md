---
date: 2026-05-12
topic: sprint-2-redux-inbound
---

# Sprint-2-redux — Inbound module + Inventory bin/zone extension + real RabbitMQ

## Summary

Sprint-2-redux ships the Inbound module (PO + per-line receiving + reconciliation tickets) plus a schema extension to Inventory (zones, bins, per-bin stock) and a put-away suggestion service. Operator confirms receiving per line; each line emits an `InboundConfirmed` event via real RabbitMQ-backed MassTransit, consumed by Inventory to auto-create `stock_items` (if new SKU) and apply a bin-targeted stock adjustment. Quantity mismatch creates an append-only reconciliation ticket. The MassTransit transport flips from in-memory → RabbitMQ in this sprint (promoted from W6 to W4).

---

## Problem Frame

The Phase-0-redux foundation + Sprint-1-redux reservation ledger ship a system that can hold stock and reserve it under flash-sale load, but stock arrives in the warehouse via a path that today is entirely manual — direct INSERTs into `stock_items` from seed scripts and tests. The warehouse operator persona (Product Plan §3.1) has no first-class flow for: receiving an inbound shipment against a known PO, recording what physically arrived, identifying mismatches, or being told where to put the units. Without this flow, the system cannot operate a real warehouse — Sprint-1-redux's reservation correctness has nothing to reserve against beyond test data.

A second pain emerges at the architecture level. All inter-module communication so far has used MassTransit's in-memory transport (per ADR-0002 W1-W5 stance). Sprint-2-redux is the first cross-module write flow (Inbound → Inventory), and in-memory transport does not exercise the failure modes — network partitions, broker downtime, message redelivery, consumer crash mid-handle — that Sprint-3's fulfillment saga will absolutely depend on. Shipping real RabbitMQ as part of this sprint means Sprint-3 inherits a production-shape broker rather than discovering its sharp edges during saga work.

---

## Actors

- A1. **Warehouse Operator**: Creates POs, executes physical receiving against POs, enters actual quantity per line, accepts or overrides the system's put-away bin suggestion.
- A2. **Inbound Module API**: Owns the PO lifecycle, persists receivings + receiving_lines, emits `InboundConfirmed` per confirmed line into its outbox.
- A3. **Inventory Module Consumer**: Subscribes to `InboundConfirmed` via RabbitMQ, applies stock changes inside the tenant DB (auto-creating `stock_items` on first occurrence), emits `StockChangedEvent` into Inventory's outbox.
- A4. **Inventory Module Put-Away Service**: Read-only service that ranks candidate bins for a given SKU+quantity request based on home-zone rules + bin occupancy.
- A5. **Reconciliation Ticket Reader** (deferred resolution UI; this sprint only writes tickets): Future operator persona that will close discrepancy tickets in Sprint-2.5 / Phase-2.

---

## Key Flows

- F1. **Create PO**
  - **Trigger:** Operator creates a draft PO with supplier + expected line items (SKU + expected_qty).
  - **Actors:** A1, A2.
  - **Steps:** Operator POSTs PO with `Draft` status → operator transitions PO to `Open` when ready to receive → PO is now visible for receiving.
  - **Outcome:** A PO row in `Open` state with N line items each carrying `expected_qty` and `received_qty=0`.
  - **Covered by:** R1, R2, R3.

- F2. **Receive a line**
  - **Trigger:** Operator scans / selects an `Open` PO and starts a receiving session.
  - **Actors:** A1, A2, A4.
  - **Steps:**
    1. Operator selects a line + enters `actual_qty` (may equal, exceed, or fall short of expected_qty)
    2. Inbound API calls A4 (Put-Away Service in Inventory) synchronously to fetch top-3 bin candidates for the SKU + actual_qty
    3. Operator accepts the top suggestion OR overrides with a different bin
    4. Operator confirms — Inbound API writes `receiving` + `receiving_line` rows + (if `actual_qty != expected_qty`) a `reconciliation_ticket` row + an outbox row carrying the `InboundConfirmed` payload, all in one tenant-DB transaction
    5. Inbound's multiplexed outbox dispatcher publishes `InboundConfirmed` via RabbitMQ
    6. Line's `received_qty` updates on the PO; if all lines have `received_qty == expected_qty` PO transitions to `Closed`, else `PartiallyReceived`
  - **Outcome:** A `receiving_line` row persisted in Inbound + a published event awaiting Inventory consumption.
  - **Covered by:** R4, R5, R6, R7, R8, R9, R12.

- F3. **Inventory applies stock change**
  - **Trigger:** `InboundConfirmed` event published by Inbound arrives at Inventory's RabbitMQ consumer.
  - **Actors:** A3.
  - **Steps:**
    1. Consumer middleware reads `tenant_id` header, binds `RequestContext` to that tenant, opens a scope
    2. Consumer reads payload (`po_id`, `line_id`, `receiving_id`, `sku`, `actual_qty`, `bin_id`, `occurred_at`)
    3. Idempotency check via persisted dedup key `(receiving_id, line_id)` — if already processed, ACK and return
    4. UPSERT `stock_items` (sku) with `available=0, reserved=0` on conflict do nothing
    5. UPSERT `stock_item_bins` (sku, bin_id) — increment `quantity` by `actual_qty`
    6. UPDATE `stock_items.available += actual_qty` (the aggregate)
    7. INSERT outbox row for `StockChangedEvent`
    8. ACK message
  - **Outcome:** Stock count in tenant Inventory DB reflects the receiving, atomically with idempotency persistence.
  - **Failure path:** If consumer crashes between step 4 and step 8, the message redelivers; the dedup key check in step 3 prevents double-application. If the bin row UPSERT conflicts unexpectedly, transaction rolls back and message goes to retry → eventually DLQ after N tries.
  - **Covered by:** R10, R11, R13, R14, R15, R16, R17.

- F4. **Operator overrides put-away suggestion**
  - **Trigger:** During F2 step 3, operator selects a bin different from the top suggestion.
  - **Actors:** A1, A2.
  - **Steps:** Operator picks any active bin from the same warehouse (not necessarily in SKU's home zone); Inbound records both the suggested bin and the actual bin in `receiving_line` for audit; `InboundConfirmed` carries the actual chosen bin.
  - **Outcome:** Stock lands in operator-chosen bin; audit trail captures the override.
  - **Covered by:** R7, R8.

---

## Requirements

**PO lifecycle and persistence**
- R1. PO has states `Draft → Open → PartiallyReceived → Closed`, plus `Cancelled` as an alternate terminal from `Draft` or `Open`. State transitions enforce one-way directionality; `Closed` and `Cancelled` are terminal.
- R2. A PO carries supplier reference, expected delivery date, and N line items; each line carries SKU, expected_qty, and a running `received_qty`.
- R3. PO and line rows live in the Inbound module's per-tenant DB (`purchase_orders`, `purchase_order_lines`); no `tenant_id` column (per ADR-0003).

**Receiving and per-line confirmation**
- R4. Receiving is per-line: a single `receiving` session confirms one or more lines, and a PO may have multiple receiving sessions over time (supports partial delivery).
- R5. Each line confirmation captures `actual_qty`, `suggested_bin_id`, `actual_bin_id`, operator id, occurred_at UTC. Both bin ids retained for audit when operator overrides.
- R6. Idempotency anchor for receiving: composite `UNIQUE(receiving_id, line_id)` on `receiving_lines`. Duplicate confirmation attempts surface as no-op success.
- R7. Operator may override the system-suggested bin with any active bin in the same warehouse; the override is recorded but not blocked.
- R8. PO transitions to `PartiallyReceived` when any line has `received_qty > 0` and at least one line has `received_qty < expected_qty`; transitions to `Closed` when every line has `received_qty == expected_qty`. (Overage — `received_qty > expected_qty` — counts as Closed; the surplus is captured via reconciliation ticket per R9.)

**Discrepancy handling**
- R9. When a line is confirmed with `actual_qty != expected_qty`, the system writes an append-only `reconciliation_tickets` row with `Open` status carrying `(po_id, line_id, receiving_id, expected_qty, actual_qty, occurred_at)`. The receiving itself succeeds regardless of discrepancy direction; no blocking. Resolution workflow is deferred (Scope Boundaries).

**Cross-module event contract**
- R10. Inbound emits one `InboundConfirmed` event per confirmed line. Payload carries `(po_id, line_id, receiving_id, sku, actual_qty, bin_id, tenant_id, occurred_at)`. The outbox row is INSERTed in the same Inbound DB transaction as the `receiving_line` row.
- R11. The Inventory consumer is idempotent across redelivery: a persisted dedup key on `(receiving_id, line_id)` (an `inbound_dedup` table or equivalent in the Inventory tenant DB) ensures a redelivered message is acknowledged without re-applying stock.
- R12. The MassTransit transport for Sprint-2-redux is **real RabbitMQ**, not in-memory. The Aspire AppHost's existing RabbitMQ container becomes load-bearing for `task up`. ADR-0002's W6 transport flip is promoted to W4 with a postscript on that ADR.

**Inventory schema extension**
- R13. Inventory schema gains three tables: `zones` (zone_id PK, name, warehouse_id), `bins` (bin_id PK, zone_id FK, name, capacity, occupancy_qty), `stock_item_bins` (sku FK, bin_id FK, quantity, composite PK). Plus a `home_zone_id` nullable FK on `stock_items` for the put-away algorithm.
- R14. `stock_items.available` remains the per-SKU aggregate. `stock_item_bins.quantity` is the per-bin breakdown. Invariant: `SUM(stock_item_bins.quantity WHERE sku=X) == stock_items.available + stock_items.reserved` for every X with at least one bin row. Reservation flow (Sprint-1-redux) continues to operate against the aggregate; bin-level inventory does not change reservation semantics for this sprint.
- R15. The Inventory consumer auto-creates a `stock_items` row (`available=0, reserved=0, home_zone_id=NULL`) on first `InboundConfirmed` for a previously unknown SKU, idempotently (`INSERT ... ON CONFLICT (sku) DO NOTHING`).

**Put-away suggestion**
- R16. Inventory exposes a synchronous read endpoint that, given a SKU + quantity, returns the top-3 bin candidates ranked by `(zone_priority, available_capacity DESC, current_occupancy ASC)` where `available_capacity = bin.capacity - bin.occupancy_qty`. Zone priority: if `stock_items.home_zone_id` is set, bins in that zone rank first; otherwise zone priority is uniform.
- R17. Put-away suggestion is queried synchronously by Inbound's API during F2 step 2. In the current modular-monolith stance (W1-W5) this is an in-process call; the same endpoint shape works when the W6 host split flips it to a real HTTP call.

**Tests and gates**
- R18. Unit tests cover Domain state machines for PO, Receiving, ReceivingLine (transitions, invariant violations) and the Reconciliation ticket creation rule. Run on every PR; no Docker required.
- R19. Integration tests use Testcontainers Postgres + Testcontainers RabbitMQ. Tagged `Category=Integration`. Coverage includes: per-line receiving happy path, partial-delivery PO state, discrepancy ticket creation, operator bin override, Inventory consumer auto-create-stock_items, consumer redelivery idempotency, put-away top-3 ranking against known bin state.
- R20. A **cross-module flow test** exercises the full Inbound → RabbitMQ → Inventory chain against real Postgres + real RabbitMQ in Testcontainers; asserts the receiving lands in Inbound, the event publishes, the Inventory consumer applies the stock change, and `StockChangedEvent` lands in Inventory's outbox.

---

## Acceptance Examples

- AE1. **Covers R4, R5, R6, R8.** Given a PO `PO-1` with one line `(sku=SKU-A, expected_qty=100, received_qty=0)` in `Open` state, when the operator confirms a receiving with `actual_qty=60` and `bin_id=B-7`, then `receiving_lines` gains one row keyed by `(receiving_id, line_id)`, `purchase_order_lines.received_qty` for that line is 60, the PO state becomes `PartiallyReceived`, and a second confirmation of the same `(receiving_id, line_id)` returns success without writing a second row.
- AE2. **Covers R9.** Given the same PO line, when the operator confirms `actual_qty=95` (expected 100), then a `reconciliation_tickets` row is written with `status=Open`, `expected_qty=100`, `actual_qty=95`, and the receiving still succeeds.
- AE3. **Covers R9.** Given the same PO line, when the operator confirms `actual_qty=110` (overage), then a `reconciliation_tickets` row is written and the line is treated as fully received (`received_qty=110`); the PO can close.
- AE4. **Covers R10, R11, R15.** Given an `InboundConfirmed` event for a SKU `SKU-NEW` that does not exist in `stock_items`, when the Inventory consumer processes it the first time, then `stock_items` gains a row `(sku=SKU-NEW, available=actual_qty, reserved=0)`, `stock_item_bins` gains a row for the chosen bin, and `inbound_dedup` records the `(receiving_id, line_id)`. When the same message redelivers, the consumer ACKs without further writes.
- AE5. **Covers R16, R17.** Given an Inventory state with three bins for `SKU-A`'s home zone with `(capacity, occupancy)` of `(100, 80)`, `(100, 20)`, `(100, 50)`, when Inbound queries put-away for `SKU-A, qty=30`, then the response ranks `(100, 20)` first (available_capacity=80), then `(100, 50)` (avail=50), then `(100, 80)` (avail=20).
- AE6. **Covers R7.** Given the put-away suggestion ranks bin `B-1` first, when the operator picks `B-2` instead, then `receiving_line.suggested_bin_id=B-1`, `receiving_line.actual_bin_id=B-2`, and the `InboundConfirmed` event payload carries `bin_id=B-2`.

---

## Success Criteria

- Warehouse operator persona can complete an end-to-end inbound flow: create a PO, receive lines (with mismatches and overrides), see stock land in the correct bin, and observe partial-delivery state on the PO — without manual SQL anywhere on the path.
- The first cross-module write flow (Inbound → Inventory) runs over real RabbitMQ in dev (`task up`) and in CI integration tests; redelivery and consumer crash recovery scenarios pass without double-applying stock.
- A downstream developer reading the requirements + Tech Design §11.3 can produce a complete implementation plan without inventing event payload fields, idempotency keys, PO lifecycle transitions, or bin-ranking criteria.
- Sprint-3-redux (Outbound + saga) inherits a production-shape RabbitMQ broker and a working bin-aware Inventory write path — no broker work in Sprint-3.

---

## Scope Boundaries

- **Reconciliation ticket resolution workflow** (operator closes a ticket, retroactively adjusts a PO, or writes a correction stock adjustment): deferred to Sprint-2.5 or Phase-2 admin surfaces. Sprint-2-redux only writes `Open` tickets.
- **"Add SKU" admin endpoint** (an explicit product master surface for creating empty SKU rows): deferred to Phase-2. Auto-create on first receive covers Sprint-2-redux needs.
- **StockItemRepository remaining stubs** (`FindBySkuAsync`, non-bin `AdjustAsync`): Sprint-3-redux closes them for Outbound's picking flow.
- **Configurable discrepancy thresholds** (e.g., ±10% auto-accept, larger blocks): Phase-2 if real need surfaces.
- **PO import from CSV / external API**: Phase-3 or never within the 12-week portfolio scope.
- **Velocity-based / ABC slotting algorithms**: out of the 12-week portfolio scope; the put-away algorithm remains zone+capacity ranked.
- **Multi-warehouse per tenant**: Phase-3+. This sprint assumes one warehouse per tenant; `warehouse_id` on `bins` is recorded but not used as a query dimension.
- **Returns inflow** (return-restock back to bins): Phase-2 Sprint-4 alongside channel adapters (Shopee/Lazada returns webhooks).
- **Kafka transport switch**: rejected against RabbitMQ for ShopFlow's workload shape (saga semantics canonical on RabbitMQ, per-tenant routing trivial via headers, operational simplicity for single-developer portfolio, message volume ~30 msgs/sec peak nowhere near Kafka's leverage point). Documented in Key Decisions.
- **CDC / Debezium event log replay**: Tech Design v3.0 §5.3 Mode C; Phase-3+ infrastructure.

---

## Key Decisions

- **Receiving granularity: per-line, multiple receivings per PO.** Rationale: real SEA marketplace flow ships partial deliveries; whole-PO receiving would force unnatural batching and force ops to wait for a complete delivery before recording any.
- **Discrepancy: auto-accept actual + append-only ticket.** Rationale: blocking on discrepancy stalls warehouse ops (the operator has the units in hand and must record them); Product Plan §9.3 specifies "auto-creates a reconciliation ticket". Resolution is a separate workflow concern.
- **Bin tracking lives in Inventory module, not Inbound.** Rationale: stock state is Inventory's domain; Inbound is a movement, not a storage authority. Sprint-3 picking will need per-bin stock visibility, so investing in the schema now avoids retroactive schema migration later.
- **`InboundConfirmed` carries operator-chosen `bin_id`.** Rationale: Inbound's API drives the put-away decision (with the operator) and ships the result. Inventory consumer applies stock to that bin directly without re-running put-away suggestion on the Inventory side.
- **Idempotency key for the Inventory consumer: persisted `(receiving_id, line_id)` dedup row, not message-id.** Rationale: the business identity of an inbound event is the receiving-line, not the broker's message id. Replays from outbox redrive or operator-initiated re-publish must still dedupe correctly.
- **Real RabbitMQ in this sprint (W6 → W4 promotion).** Rationale: Sprint-2-redux is the first cross-module write flow; in-memory transport doesn't exercise the failure modes (network partition, consumer crash mid-handle, redelivery) that Sprint-3 saga will depend on. Shipping production broker now means Sprint-3 inherits a tested transport rather than discovering broker behavior under saga complexity.
- **RabbitMQ over Kafka.** Rationale: ShopFlow workload (~30 msgs/sec peak, 50 tenants, saga state machines, per-tenant routing via headers) is squarely in RabbitMQ's strength zone. Kafka would win only at 10K+ msgs/sec or if CDC replay were needed; both are Phase-3+ concerns. Operational simplicity matters for single-developer ops.
- **Auto-create `stock_items` on first receive.** Rationale: avoids a separate product master admin surface in Sprint-2 while preserving correctness (the auto-created row has `available=0, reserved=0` and gains stock only via the actual receive). Phase-2 can layer an admin endpoint when product master management becomes a first-class workflow.

---

## Dependencies / Assumptions

- Foundation tag `v0.3.0-sprint-1-redux` is the cut point; this sprint's branch (`feat/phase-1-sprint-2-redux-inbound`) is already cut.
- Aspire AppHost's RabbitMQ container (shipped in Phase-0-redux U7) becomes load-bearing — `task up` depends on it for any local cross-module flow. Confirmed wired; no new infra container needed.
- AGENTS.md §10 ("Cross-module reads never hit another module's DbContext; communicate via MassTransit") locks the cross-module write path to events; in-process direct mutation between Inbound and Inventory is non-compliant and not considered.
- The existing `MultiplexedOutboxDispatcher<TContext>` pattern (SharedKernel) covers Inbound's outbox dispatch; a new instantiation for `InboundDbContext` is required but the dispatcher code itself is reused.
- `ShopFlow.Contracts` project (or equivalent shared-contract project) holds the `InboundConfirmed` event record. Verify the project exists; if not, create it as the canonical home for cross-module integration events (AGENTS.md §6 implies it but does not enumerate the project — flag if missing).
- The `IPubSubEndpoint` / MassTransit publish endpoint resolves correctly under both in-memory (unit tests) and RabbitMQ (integration tests) transports; `AddShopFlowDefaults` gains a configuration knob to switch between them.
- Reconciliation ticket resolution workflow is committed as Sprint-2.5 or Phase-2 work — assumed to ship in the 12-week portfolio. If that commitment slips, Sprint-2-redux's tickets accumulate as a known-defect log only.

---

## Outstanding Questions

### Resolve Before Planning

(None — Phase 2.5 confirmed scope.)

### Deferred to Planning

- [Affects R10][Technical] Exact event-namespace / type for `InboundConfirmed`: is it `ShopFlow.Contracts.Inbound.InboundConfirmed` or `ShopFlow.Inbound.Domain.Events.InboundConfirmed`? Convention check during planning against the SharedKernel outbox interceptor's type-serialization pattern.
- [Affects R12][Technical] RabbitMQ connection string source: a new `RabbitMq:ConnectionString` config section, or reuse the Aspire-injected connection string convention used for Postgres/Redis? Planning resolves against existing `AddShopFlowDefaults` patterns.
- [Affects R13][Technical] Migration ordering: the Inventory schema extension is the first migration after `20260512000001_InitialInventorySchema`. Confirm migration timestamp + ensure `MigrationSmokeTests` still passes the named-tables assertion after the extension lands.
- [Affects R16][Needs research] Put-away ranking under tie-break: when two bins have identical `available_capacity` and `current_occupancy`, what's the tiebreaker — bin name lex order, oldest bin first, or random? Lex order is the cheap deterministic default; flag if a more meaningful tiebreaker emerges during planning.
- [Affects R19][Technical] Testcontainers RabbitMQ image pin (3-management-alpine per AGENTS.md §8.56) and startup time impact on per-PR CI budget. Planning measures and decides whether the cross-module flow test stays per-PR or moves to nightly.
- [Affects R20][Needs research] In-memory MassTransit test override pattern: existing unit tests register `bus.UsingInMemory(...)`. The RabbitMQ flip needs a config-driven branch in `AddShopFlowDefaults` so unit tests don't need a broker. Planning picks the cleanest config seam.
