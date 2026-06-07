---
date: 2026-05-20
sprint: sprint-8.5
problem_type: build_error
severity: medium
modules: [Contracts, Outbound, Channel, Inventory]
tags: [contract-evolution, cross-module, integration-tests, sprint-7-carry-over]
---

# Cross-module contract evolution — consumer-test sweep discipline

## Problem

`tests/ShopFlow.Outbound.IntegrationTests/Sagas/SagaTransitionsAuditFlowTests.cs` shipped at Sprint-7 referencing two contract shapes that no longer match the canonical definitions in `src/Shared/ShopFlow.Contracts/Inventory/`:

```csharp
// CS0246: "type StockReservedLineOutcomeV1 not found"
new StockReservedLineOutcomeV1("L1", "SKU-A", 1, "reserved")

// CS1739: "StockReservationFailedV1 does not have a parameter named 'Reason'"
new StockReservationFailedV1(
    OrderId: orderId,
    TenantId: tenantId,
    Reason: "insufficient_stock",
    OccurredAt: DateTime.UtcNow
)
```

The canonical Sprint-5/7 contracts:

```csharp
// LineOutcomeV1 (NOT StockReservedLineOutcomeV1 — name simplified to be
// reusable across StockReserved + StockReservationFailed; the type lives
// at src/Shared/ShopFlow.Contracts/Inventory/StockReservedV1.cs)
public sealed record LineOutcomeV1(
    string OrderLineId,
    string Sku,
    Guid? ReservationId,  // null on OVERSOLD; ledger row id on Reserved
    string Status         // "Reserved" | "Oversold"
);

public sealed record StockReservationFailedV1(
    Guid OrderId,
    Guid TenantId,
    IReadOnlyList<LineOutcomeV1> LineOutcomes,  // not a flat Reason string
    DateTime OccurredAt
);
```

The contract changed shape between Sprint-5 (single-level `Reason` string) and Sprint-7 (per-line `LineOutcomeV1` array — supports the atomic-failure case where one line oversold but per-line forensics still help) but only the producer side was updated; the consumer test in Outbound.IntegrationTests kept the old shape.

The errors stayed latent because:
1. CI's per-csproj matrix builds Outbound.IntegrationTests in isolation but the Sprint-7 changes happened to leave the test compiling (briefly — until Sprint-8 also added unrelated logging changes that surfaced the AddProvider missing extension)
2. The test was Category=Integration so it never ran on every PR

## Fix

Updated the test to match the canonical contract:

```csharp
LineOutcomes: new[] { new LineOutcomeV1("L1", "SKU-A", Guid.NewGuid(), "Reserved") },
// ...
new StockReservationFailedV1(
    OrderId: orderId,
    TenantId: tenantId,
    LineOutcomes: new[] { new LineOutcomeV1("L1", "SKU-A", null, "Oversold") },
    OccurredAt: DateTime.UtcNow
)
```

`LineOutcomeV1.ReservationId` is non-null on success (`Guid.NewGuid()` here as a placeholder — real production code reads it from the inserted ledger row), null on Oversold. `LineOutcomeV1.Status` is `"Reserved"` or `"Oversold"` — kept as a string in the contract so the contract doesn't take a Domain dependency (per the existing remark in StockReservedV1.cs).

## Pattern (apply going forward)

When changing a `src/Shared/ShopFlow.Contracts/*.cs` record's ctor signature:

1. **Before the contract change lands**, `git grep` the type name across `src/` AND `tests/` to find every consumer:
   ```
   git grep -l "<ContractTypeName>" src/ tests/
   ```

2. **List the consumer touches in the PR description** — surfaces the cross-module reach + lets reviewers spot missed sites.

3. **For renames** (here: `StockReservedLineOutcomeV1` → `LineOutcomeV1`): keep a one-cycle alias if production code is paused mid-deploy. Sprint-7 didn't, which was fine because Sprint-7 also changed the data shape (added `LineOutcomes` array to `StockReservationFailedV1`) — partial migration wasn't possible. Document the breakage in the commit body so consumers can update in lockstep.

4. **Treat `Category=Integration` tests as PR gates for contract-touching PRs**: the per-PR CI flow should run `dotnet build ShopFlow.sln` (compile-only) to catch this class of error before merge, even if the integration tests themselves are nightly. Sprint-8.5's verification gate (R13: `dotnet build ShopFlow.sln` returns 0 errors) makes this an explicit invariant going forward.

## Cross-references

- `src/Shared/ShopFlow.Contracts/Inventory/StockReservedV1.cs` — canonical `StockReservedV1` + `LineOutcomeV1` definitions
- `src/Shared/ShopFlow.Contracts/Inventory/StockReservationFailedV1.cs` — canonical failed-reservation shape with `LineOutcomes` array
- Sprint-7 plan KTD3 + KTD4 — the saga-observer test pattern this file exercises
- `docs/CHANGELOG.md` — contract-version-drift register (not yet documented; Sprint-9+ candidate for a per-version log)
