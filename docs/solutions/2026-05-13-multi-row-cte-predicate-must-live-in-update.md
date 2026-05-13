---
title: "Multi-row conditional CTE: the availability predicate must live INSIDE the UPDATE, not in a pre-check CTE"
date: 2026-05-13
status: resolved
context: Sprint-3-redux U3
related:
  - docs/solutions/2026-05-12-readcommitted-conditional-cte-correctness.md
  - docs/plans/2026-05-13-002-feat-phase-1-sprint-3-redux-outbound-plan.md
---

# Multi-row conditional CTE: predicate must live in the UPDATE, not a pre-check CTE

## TL;DR

The Sprint-3-redux plan's K11 pseudocode for `TryReserveLinesAsync` proposed checking line availability in a `will_succeed` CTE *before* the UPDATE that decrements `stock_items.available`. **This shape is unsafe under READ COMMITTED concurrency**: two transactions can both pass the pre-check before either commits, and then both UPDATEs run blindly because the predicate doesn't re-evaluate against the post-lock snapshot. Caught by Sprint-1-redux's `TryReserve_ConcurrentOversell_AtMostAvailableSucceed` test — 30 callers × `qty=60` against `available=1000` returned 30 successes instead of the structural cap of ≤16 (1000 / 60 = 16.67, can't exceed 16 without oversell).

Fix: move the availability predicate **inside** the UPDATE's WHERE clause and gate the INSERT with a `NOT EXISTS` aggregate over the actually-deducted set. This matches Sprint-1-redux's single-line conditional-CTE pattern (`docs/solutions/2026-05-12-readcommitted-conditional-cte-correctness.md`) — the single-line shape was already correct; only the multi-line extension needed careful translation.

## What didn't work

Original K11 pseudocode:

```sql
WITH desired AS (VALUES ...),
     will_succeed AS (                              -- ❌ pre-check, no locks held
       SELECT bool_and(si.available >= d.qty) AS ok
       FROM desired d JOIN stock_items si ON si.sku = d.sku
     ),
     deducted AS (
       UPDATE stock_items SET available = available - d.qty
       FROM desired d
       WHERE stock_items.sku = d.sku
         AND (SELECT ok FROM will_succeed)         -- ❌ stale predicate
       RETURNING stock_items.sku
     ),
     inserted AS (
       INSERT INTO reservations_ledger (...)
       SELECT @order_id, d.line_id, ...
       FROM desired d
       WHERE (SELECT ok FROM will_succeed)
       RETURNING *
     )
SELECT * FROM inserted;
```

The two failure modes:

1. **Stale snapshot at pre-check time.** `will_succeed` SELECTs `available` *before* the UPDATE acquires row locks. Under READ COMMITTED each statement sees the latest committed snapshot, but the SELECT runs at the start of the CTE before any row lock — two concurrent transactions can both read `available=1000` and both compute `ok=true`. Then both UPDATEs run, decrementing 1000 → 940 → 880; concurrent doer reads 1000 then sets to 940; both think they succeeded.

2. **The `(SELECT ok FROM will_succeed)` subquery doesn't recompute under the UPDATE's locks.** Postgres reads `ok` from the planned CTE materialization, not from a fresh evaluation. By the time the UPDATE WHERE clause locks the row, the predicate value is frozen.

Net effect: 30 callers all pass the pre-check, all UPDATE, `stock_items.available` goes negative, all INSERTs succeed → 30 reservation rows for an inventory that could only support 16.

## What worked

```sql
WITH desired(sku, order_line_id, qty, reservation_id) AS (VALUES (...), ...),
     desired_per_sku AS (
       SELECT sku, SUM(qty)::int AS total_qty
         FROM desired
        GROUP BY sku
     ),
     deducted AS (
       UPDATE stock_items si
          SET available  = si.available - dps.total_qty,
              reserved   = si.reserved + dps.total_qty,
              updated_at = @p_now
         FROM desired_per_sku dps
        WHERE si.sku = dps.sku
          AND si.available >= dps.total_qty       -- ✅ predicate INSIDE the UPDATE
       RETURNING si.sku
     ),
     all_succeeded AS (
       SELECT 1 AS ok
        WHERE NOT EXISTS (                         -- ✅ every requested sku must be in `deducted`
          SELECT 1 FROM desired_per_sku dps
           WHERE NOT EXISTS (SELECT 1 FROM deducted d WHERE d.sku = dps.sku)
        )
     ),
     inserted AS (
       INSERT INTO reservations_ledger (id, sku, order_id, order_line_id, quantity, status, expires_at, created_at)
       SELECT d.reservation_id, d.sku, @p_order, d.order_line_id, d.qty, 'Pending', @p_expires, @p_now
         FROM desired d
        WHERE EXISTS (SELECT 1 FROM all_succeeded) -- ✅ atomic gate
       RETURNING id, sku, order_line_id, quantity
     )
SELECT id, sku, order_line_id, quantity FROM inserted;
```

Key properties:

- **Predicate inside the UPDATE WHERE.** The UPDATE acquires row locks before evaluating `available >= qty`. The locked rows return the latest committed snapshot; concurrent transactions queue on the lock and re-evaluate after the prior transaction commits. This is the standard READ COMMITTED row-level serialization guarantee.

- **`all_succeeded` enforces atomicity post-deducted.** Even if some skus pass and others fail in the UPDATE (because two of the requested skus have insufficient available, say), the INSERT is skipped because not every `desired_per_sku.sku` appears in `deducted`.

- **Caller-side rollback on 0-row return.** When `inserted` returns 0 rows, the caller (the consumer's EF transaction) explicit-rollbacks. The partial UPDATEs unwind cleanly because Postgres MVCC versions them in the aborted transaction. Other transactions waiting on the locked rows resume under the rolled-back values.

- **`desired_per_sku` aggregation handles same-sku-multi-line.** If an order has two lines with the same SKU (e.g., a kit purchase), the SUM aggregation ensures the predicate is checked against the combined qty, not each individual line.

## Why the single-line shape didn't have this bug

Sprint-1-redux's single-line `TryReserveAsync` puts the predicate inside the UPDATE from day one:

```sql
WITH deducted AS (
  UPDATE stock_items SET available = available - @qty
   WHERE sku = @sku AND available >= @qty       -- predicate in UPDATE
  RETURNING sku
),
inserted AS (
  INSERT INTO reservations_ledger (...)
  SELECT ... WHERE EXISTS (SELECT 1 FROM deducted)
  RETURNING *
)
SELECT * FROM inserted;
```

The single-line case naturally has no "atomic across multiple rows" concern — one row succeeds or fails. The multi-line extension needed to preserve the in-UPDATE predicate AND add a new gate (`all_succeeded`) for atomicity across the row set. K11's pseudocode tried to factor "did all lines succeed?" out of the UPDATE for readability — that factoring broke the concurrency guarantee.

## Detection

The bug surfaced via the existing Sprint-1-redux test suite, specifically `ReservationRepositoryTests.TryReserve_ConcurrentOversell_AtMostAvailableSucceed`:

```csharp
// 30 concurrent reservations of qty=60 against stock available=1000
// Structural cap: floor(1000 / 60) = 16 successes
const int callers = 30, qty = 60, initial = 1000;
var tasks = Enumerable.Range(0, callers).Select(i => Task.Run(...));
var results = await Task.WhenAll(tasks);
int successes = results.Count(r => r.IsSuccess);
successes.Should().BeLessThanOrEqualTo(16);  // FAILED: was 30
```

The test was already in the codebase, so the bug couldn't ship. Without it, the multi-line CTE would have silently allowed oversell — exactly the correctness failure Sprint-1-redux's reservation ledger exists to prevent.

## Lessons

1. **Predicates in conditional CTEs must live inside the row-level lock.** Any `WHERE` clause that gates a state transition needs to be evaluated under the UPDATE's locks. Moving it to a pre-check CTE (for readability or aggregation) breaks the snapshot guarantee.

2. **Multi-row extensions of single-row patterns need fresh concurrency review.** The single-line shape's correctness doesn't extend automatically. Reviewers (human or AI) should specifically ask: "what happens with N concurrent transactions on the same row set?"

3. **Existing scale-gate / oversell tests are load-bearing assets.** Sprint-1-redux's concurrent-oversell test wasn't gated to single-line callers — when U3's `TryReserveLinesAsync` wrapped the single-line API, the test ran against the new multi-line CTE underneath. That's why this was caught at U3 time, not at U4/U8 time.

## Follow-ups

- **Plan K11 prose updated** in `docs/plans/2026-05-13-002-feat-phase-1-sprint-3-redux-outbound-plan.md` to reflect the corrected shape. Future implementers see the correct pattern from the plan, not the original pseudocode.

- **Possible ShopFlow0005 analyzer**: detect raw-SQL or EF FromSql containing a `will_succeed`-style pre-check CTE pattern where the same predicate is referenced in both the pre-check and the UPDATE/INSERT. Low confidence (heuristic detection is hard), but worth considering if this defect recurs. Lower-effort alternative: an AGENTS.md rule under §3 (Migrations / data access) explicitly forbidding the pre-check pattern.

- **`MaintainabilityReviewer` prompt hint**: when reviewing data-mutation code, flag any conditional CTE where the gating predicate appears in a CTE other than the mutating statement itself. Backport to compound-engineering review prompts.
