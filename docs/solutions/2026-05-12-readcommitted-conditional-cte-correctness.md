---
title: "READ COMMITTED + conditional-CTE INSERT for reservation correctness"
date: 2026-05-12
status: active
tags: [postgres, isolation, reservation-ledger, sprint-1-redux]
---

# READ COMMITTED + conditional-CTE INSERT for reservation correctness

## The trap

Reading Postgres' "Concurrency Control" chapter and the v2.0 Tech Design (which named SERIALIZABLE for the reservation flow) suggests SERIALIZABLE is the safe default for "conditional INSERT only if invariant holds." It is not — for the reservation ledger pattern, it is overkill that pays serialization-failure retry cost (`40001`) without giving any correctness benefit beyond what READ COMMITTED already provides, **provided the SQL is shaped correctly**.

The correctness-bearing primitive in `ReservationRepository.TryReserveAsync` is the data-modifying CTE:

```sql
WITH upd AS (
    UPDATE stock_items
       SET available  = available - @qty,
           reserved   = reserved + @qty,
           updated_at = @now
     WHERE sku = @sku
       AND available >= @qty
    RETURNING sku
)
INSERT INTO reservations_ledger (...)
SELECT ... FROM upd
RETURNING id;
```

Two reasons this is race-free under READ COMMITTED:

1. **The UPDATE takes a row lock on `stock_items.sku`.** Two concurrent UPDATEs on the same row serialize via Postgres' row-level locking. The second waits until the first commits, then re-reads the row's current state, sees the new `available`, and decides for itself whether the predicate holds.
2. **The INSERT is conditional on the UPDATE producing a row** (`SELECT ... FROM upd`). If the UPDATE rejects the predicate, `upd` is empty, the INSERT inserts zero rows, `RETURNING id` yields no value, and the caller gets `null` from `ExecuteScalarAsync` → maps to `Result.Failure("oversold", "reservation.insufficient_stock")`.

SERIALIZABLE would add: detection of broader anomalies (lost update, write skew on _different_ rows). The reservation ledger doesn't have those — every contender for a given SKU competes on the SAME `stock_items` row. The row lock is sufficient.

## Why this matters (cost side)

SERIALIZABLE under contention surfaces as `40001` serialization failures. The application is expected to catch and retry. Two practical problems:

- Retry budget is finite — under sustained flash-sale load the failure rate climbs and tail latency explodes.
- Retries are observable to the user: same `order_id` may be tried by the client multiple times, and if the saga decides to abandon an attempt the client sees an unrelated failure.

Per the v3.0 Tech Design correction (§4.4) and ADR-0003, ShopFlow ships the READ COMMITTED + conditional-CTE pattern and only escalates to `SELECT … FOR UPDATE` (also at READ COMMITTED) if a future load test finds a real race the row lock doesn't cover. We do **not** regress to SERIALIZABLE.

## Idempotency, layered

A second concurrency concern is duplicate `order_id` retries. The repository handles this in two layers:

1. **Application-level short-circuit** — `FindByOrderIdAsync(orderId)` runs before the transaction. If the row exists, return it. Common retry path; one round trip, no transaction.
2. **Database-level `UNIQUE(order_id)`** — concurrent same-order_id callers race past the short-circuit. The second INSERT trips `23505` (UniqueViolation). The repository catches `PostgresException` where `SqlState == PostgresErrorCodes.UniqueViolation`, rolls back, re-runs `FindByOrderIdAsync`, returns the existing row.

This idempotency anchor does NOT depend on the isolation level — `UNIQUE` constraints are enforced at every isolation under Postgres.

## Carry-forward rule

When a future sprint adds another conditional-write surface (e.g., Inbound's GRN bulk-receive), the pattern is:

- Identify the row that arbitrates contention.
- Make the UPDATE / SELECT-FOR-UPDATE on that row the gate.
- Conditionally INSERT off the gate's RETURNING set.
- Keep the isolation level at READ COMMITTED.
- Pair with a UNIQUE constraint for idempotency.

If a code review suggests `SERIALIZABLE` to "be safe," push back with this entry — bring real numbers, then escalate to FOR UPDATE only if the row lock genuinely doesn't cover the case.
