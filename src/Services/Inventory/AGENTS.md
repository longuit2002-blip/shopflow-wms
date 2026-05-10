# AGENTS.md — Inventory module deltas

This is the **blessed reference module**. New cross-cutting concerns flow root → here → other modules. Don't add module-specific patterns here unless they actually need to live here — generalisable patterns belong in `src/Shared/ShopFlow.SharedKernel/` and `/AGENTS.md`.

Inherits from the root canon (`/AGENTS.md`) and the kernel's `src/Shared/ShopFlow.SharedKernel/AGENTS.md`. Module-local rules:

1. Reservation lifecycle is `Active → (Confirmed | Released | Expired)`. Never `DELETE` rows from `reservations_ledger`; transitions are state-only. Audit immutability depends on this.
2. The `TryReserveAsync` SQL is load-bearing for the Phase-1 Sprint-1 oversell scale gate (5,000 concurrent reservations against 1,000 units = exactly 1,000 successes, zero oversell, p99 < 200 ms per Plan §299). Do NOT "simplify" the conditional CTE INSERT to a SELECT-then-INSERT pattern; the conditional shape is what gives serializability without locking `stock_items`.
3. Outbox is wired automatically. Domain events raised on `StockItem` are persisted by the kernel's `OutboxInterceptor` in the same transaction as the business write. The `ReservationRepository.TryReserveAsync` raw-SQL path is the one exception that hand-writes its own `outbox_messages` row — because the conditional INSERT bypasses the change tracker. Do not call `IPublishEndpoint.Publish` directly from a write handler (analyzer ShopFlow0002 will flag, and the dispatcher already publishes from the outbox).
4. `AvailableQuantity` is a derived read-side concept (Tech Design §7.5). Do not add it as a stored column on `StockItem`; the join in `StockItemRepository.GetAvailabilityAsync` is canonical.
5. Time goes through `TimeProvider` (registered via `InventoryServiceCollectionExtensions`). Never call `DateTime.UtcNow` from the Application or Infrastructure layers when the value is part of behaviour or persisted data — analyzer ShopFlow0004 only catches `.Now`, but the same discipline applies to `UtcNow`.
