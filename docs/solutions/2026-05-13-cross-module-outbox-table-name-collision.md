---
title: "Cross-module outbox-table name collision in shared tenant DB"
date: 2026-05-13
status: open-known-issue
tags: [adr-0003, outbox, multi-module, sprint-2-redux, sprint-2.5-candidate]
---

# Cross-module outbox-table name collision in shared tenant DB

## What broke

Sprint-2-redux U9 attempted to write a cross-module flow integration test (Inbound emits `InboundConfirmedV1` → Inventory consumer applies stock change) against a single Testcontainers Postgres tenant DB shared by both modules (the ADR-0003 design — same physical DB, separate module schemas).

The test failed at fixture setup:

```
Npgsql.PostgresException : 42P07: relation "outbox_messages" already exists
```

Both Inbound and Inventory migrations create a table named `outbox_messages` in the `public` schema. The first migration to run wins; the second hits the collision.

## Root cause

Each module's `OutboxMessageConfiguration` (Inbound's at `src/Services/Inbound/ShopFlow.Inbound.Infrastructure/EntityConfigurations/OutboxMessageConfiguration.cs`, Inventory's at the matching path) maps the shared `OutboxMessage` type to a table named `outbox_messages`. Each module's migration creates that table. When both modules share one physical tenant DB (per ADR-0003), the namespaces collide.

The bug ships in Sprint-2-redux because:
- Phase-0-redux U8 shipped only Inventory's `outbox_messages` table — no collision.
- Sprint-1-redux added behavior but no new outbox table.
- Sprint-2-redux U1 shipped Inbound's `outbox_messages` table — first time both modules touch the same DB simultaneously.
- The migration smoke tests run each migration against a **fresh** DB, so the collision didn't surface until a cross-module flow test forced both migrations into the same DB.

## The fix (not yet applied)

Per-module table-name prefix:
- Inbound's table: `inbound_outbox_messages`
- Inventory's table: `inventory_outbox_messages`

Each module's `OutboxMessageConfiguration.ToTable(...)` declares the prefixed name; each module's migration creates the prefixed table. The `MultiplexedOutboxDispatcher<TContext>` is unaffected — it reads through EF's `_db.Set<OutboxMessage>()` which respects the entity configuration.

Alternative: Postgres schema separation (`inbound.outbox_messages`, `inventory.outbox_messages`). More structurally pure but requires `EnsureSchema` calls in every migration. Table-name prefix is the simpler fix.

## Why deferred

The fix touches Sprint-1-redux's `outbox_messages` references:
- `MigrationSmokeTests.InventoryMigration_AppliesAndLeavesNamedObjects` asserts the table name.
- `ReservationRepository.AppendOutbox` and the test scenarios that assert outbox row counts.
- `ReservationExpiryWorker` integration tests query `outbox_messages` directly.

The rename is non-trivial mechanical work plus careful test updates — a focused follow-up unit, not a Sprint-2-redux inline fix.

## What still works without the fix

- All 162 tests in Sprint-2-redux pass (110 unit + 52 integration) because each test class provisions a fresh tenant DB and only one module's migration runs per DB.
- Production Aspire / docker-compose deployments today run each module's Api in the same host but with separate DbContext factories targeting the same tenant DB — the collision would surface at first migration on a clean dev cluster the moment a tenant gets provisioned and both modules try to apply their initial schema. Phase-0-redux's `shopflow-migrate provision` runs migrations sequentially per module, so the **second** module's migration trips on a fresh tenant DB the first time someone tries to provision against both modules.
- The Sprint-2-redux U6 + U8 tests validate the cross-module contract + consumer logic via MassTransit's in-memory test harness against separate Postgres DBs (each module's IntegrationTests project has its own fixture) — no collision because the modules don't share a DB in those tests.

## Tracker

- Sprint-2-redux U9 deferred ([docs/plans/2026-05-13-001-feat-phase-1-sprint-2-redux-inbound-plan.md](../plans/2026-05-13-001-feat-phase-1-sprint-2-redux-inbound-plan.md)).
- Sprint-2.5 or Sprint-3-redux candidate: rename both modules' outbox tables to `<module>_outbox_messages`, update tests, re-attempt the cross-module flow test.
- Blocks: full end-to-end validation of the Inbound → Inventory flow against a single tenant DB (the realistic production shape).
- Workaround: Phase-2 cross-module integration testing via two separate Testcontainers Postgres databases (each module on its own DB) gives ~95% of the validation value without the rename. Acceptable for first CI green if the rename slips beyond Sprint-2.5.
