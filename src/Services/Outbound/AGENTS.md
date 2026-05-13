# AGENTS.md — Outbound module deltas

Per root AGENTS.md §11.82 this file captures Outbound-specific invariants only.

## Hard "do not simplify"

- **Saga compensation is mandatory** per Tech Design v3.0 §9. Every Reserve → Pick → Pack → Ship step has a documented `OnFailure` that releases the reservation and rolls back picked stock. Do not introduce an Outbound step without its compensation.
- **MassTransit saga state persists in Postgres** at MVP per root AGENTS.md §6.44; Redis is the scale option (and the K15 fallback if MT.EFCore 8.3.4 + EF Core 9 binding ever breaks), not the default. Do not swap the persistence in Phase-1.

## Cross-module contract

- **Outbound publishes commands** `ShopFlow.Contracts.Inventory.ReserveStockV1` / `ConfirmStockV1` / `ReleaseStockV1` via `outbound_outbox_messages`; Inventory consumers (Sprint-3-redux U3) wrap `ReservationRepository.TryReserveLinesAsync` / `ConfirmAsync` / `ReleaseLinesAsync`. Result events `StockReservedV1` / `StockReservationFailedV1` / `StockConfirmedV1` / `StockReleasedV1` flow back through `inventory_outbox_messages` to the saga.
- **`TrackingPushedV1`** is owned by Outbound (`ShopFlow.Contracts.Outbound.*`) — Channel module consumes it in Phase-2 Sprint-4. Stub `ChannelTrackingConsumer` lives in Outbound.Infrastructure for Sprint-3-redux.

## Module conventions

- Composition entry point: `services.AddShopFlowDefaults(...)` then `services.AddOutboundModule(IConfiguration)`.
- `OutboundDbContext` is constructed via the scoped registration `services.AddScoped<OutboundDbContext>(sp => ...)` reading `IRequestContext.DbConnectionString` — same pattern as Inbound / Inventory.
- Migrations live in `ShopFlow.Outbound.Infrastructure/Migrations/`. Hand-authored with `[Migration]` + `[DbContext]` attributes per AGENTS.md §3.23. `OnConfiguring` overrides `PendingModelChangesWarning` per [docs/solutions/2026-05-13-ef9-pendingmodelchangeswarning-with-hand-authored-migrations.md](../../../docs/solutions/2026-05-13-ef9-pendingmodelchangeswarning-with-hand-authored-migrations.md).
- Outbox table is `outbound_outbox_messages` per Sprint-2.5's per-module prefix convention (avoids the cross-module collision documented at [docs/solutions/2026-05-13-cross-module-outbox-table-name-collision.md](../../../docs/solutions/2026-05-13-cross-module-outbox-table-name-collision.md)).

## Sprint-3-redux U1 state

Schema + composition root only. Plan: [docs/plans/2026-05-13-002-feat-phase-1-sprint-3-redux-outbound-plan.md](../../../docs/plans/2026-05-13-002-feat-phase-1-sprint-3-redux-outbound-plan.md). `OrdersController` returns 501 — U2 fills the manual create + read endpoints. Saga + pick queue + mock carrier land in U4 / U5 / U6.
