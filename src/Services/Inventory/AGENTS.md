# AGENTS.md — Inventory module deltas

Per root AGENTS.md §11.82 this file captures Inventory-specific invariants only. Rules that apply repo-wide live in the root canon; do not restate them here.

## Hard "do not simplify"

- **Reservation ledger is append-only.** Inserts use the conditional-CTE pattern at READ COMMITTED per Tech Design v3.0 §4.4 (Sprint-1-redux). Do not switch to `SELECT … FOR UPDATE`, do not lift the isolation level, do not collapse the CTE into a plain INSERT. The pattern is the flash-sale correctness anchor.
- **Idempotency anchor is `UNIQUE(order_id)`** — NOT `UNIQUE(tenant_id, order_id)`. ADR-0003: the database is the tenant boundary. The duplicate-insert race is caught at the index, not by application code.
- **`stock_items.sku` is the PK.** Do not introduce a surrogate Guid. The inherited `BaseEntity.Id` is `Ignore`d in the EF mapping; the inherited domain-event buffer survives.
- **`row_version` is `xid`,** mapped to `uint` with `(txid_current())::text::xid` default. Do not regress to `byte[]` / `rowversion` — the conditional-INSERT depends on the txid identity.

## U8 stub state

- Repository implementations under `ShopFlow.Inventory.Infrastructure/Repositories/` throw `NotImplementedException("Sprint-1-redux …")` on every method. This is the W1 green-against-stub pattern (`docs/solutions/2026-05-10-green-against-stub-property-suite.md`). Tests against the ledger spec are red until Sprint-1-redux makes them green by implementing behavior — do **not** make the tests green by stubbing assertions.
- Domain methods that mutate aggregate state (`StockItem.Reserve/Confirm/Release/Adjust`, `Reservation.Confirm/Release/Expire`) also throw. `StockItem.Create`, `Reservation.Create`, and the value-object constructors are real.

## Module conventions

- Composition entry point: `services.AddShopFlowDefaults(...)` then `services.AddInventoryModule(IConfiguration)`. Wrong order surfaces as a missing-dependency exception at first request, not startup.
- `InventoryDbContext` is constructed via `IDbContextFactory<InventoryDbContext>` only; direct `new InventoryDbContext(...)` is ShopFlow0003.
- Migrations live in `ShopFlow.Inventory.Infrastructure/Migrations/` (not a separate Migrations project — that pattern is ControlPlane-only per U5's deviation note).
