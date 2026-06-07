---
date: 2026-05-20
sprint: sprint-8.5
problem_type: build_error
severity: low
modules: [Inventory]
tags: [namespace, using-discipline, value-objects, sprint-7.5-carry-over, skudimensions]
---

# `SkuDimensions` — using-directive discipline after Sprint-7.5 U3 namespace move

## Problem

Sprint-7.5 U3 introduced the rich `Sku` catalog and moved value-object types under `ShopFlow.Inventory.Domain.Catalog.ValueObjects` (the canonical location matching the existing `Domain.Catalog.ValueObjects/SkuDimensions.cs` file). Most consumers were updated to import the new namespace. `SkusController` was missed — it kept referencing the type via a stale fully-qualified path:

```csharp
// Compile errors at SkusController.cs(91,43) + (96,24) + (110,29):
// CS0234: "The type or namespace name 'SkuDimensions' does not exist in
//          the namespace 'ShopFlow.Inventory.Domain.Catalog'"
// CS1503: "cannot convert from 'ShopFlow.Inventory.Domain.Catalog.SkuDimensions?'
//          to 'ShopFlow.Inventory.Domain.Catalog.ValueObjects.SkuDimensions?'"
ShopFlow.Inventory.Domain.Catalog.SkuDimensions? dims = null;
//                              ^^^^^^^^^^^^^^^ — wrong; lives in .ValueObjects
```

The compiler resolves `ShopFlow.Inventory.Domain.Catalog` to a real namespace (the parent of `ValueObjects`), but `SkuDimensions` is NOT directly under it — it's one level deeper in the `.ValueObjects` sub-namespace. Without an explicit `using ShopFlow.Inventory.Domain.Catalog.ValueObjects;`, the FQN must be the full nested path.

The error stayed latent because Inventory.Api wasn't in shopflow-migrate's transitive dep tree until Sprint-8 U10 + U10 also surfaced an unrelated stray brace in `ISkuRepository.cs` that, when fixed, exposed a chain of latent errors including this one.

## Fix

`src/Services/Inventory/ShopFlow.Inventory.Api/Controllers/SkusController.cs`:
```csharp
// Add at top of file:
using ShopFlow.Inventory.Domain.Catalog.ValueObjects;

// Then:
SkuDimensions? dims = null;
// Instead of:
// ShopFlow.Inventory.Domain.Catalog.SkuDimensions? dims = null;
```

Verified there is only ONE `SkuDimensions` type in the codebase (a `grep -r "class SkuDimensions"` returns exactly `src/Services/Inventory/ShopFlow.Inventory.Domain/Catalog/ValueObjects/SkuDimensions.cs`). The "duplicate type" appearance was an illusion — the stale FQN was pointing at a non-existent namespace path, not a different type.

## Pattern (apply going forward)

When `Domain.Catalog.ValueObjects` adds a new value object:

1. Update the aggregate root + Application DTOs to import `using ShopFlow.Inventory.Domain.Catalog.ValueObjects;` — NEVER use FQN paths for value objects in business code.
2. Search the codebase for any FQN reference to the OLD location before merging the namespace move: `git grep "ShopFlow.Inventory.Domain.Catalog\.<TypeName>"`.
3. If a stale FQN survives, the compiler error surfaces as CS0234 (type not in namespace) — diagnostic; not a "type was deleted" signal.

The `Sku` aggregate vs `Sku` value object collision Sprint-7.5 resolved via `using SkuCode = ShopFlow.Inventory.Domain.Sku;` is the OTHER style; both are valid:
- **Alias** (Sprint-7.5 U3 approach): when two types share the same name across namespaces.
- **Plain using directive** (this fix): when one canonical type lives in a sub-namespace and consumers just need the import.

## Cross-references

- Sprint-7.5 U3 sign-off `docs/phase-gates/2026-05-20-sprint-7.5-signoff.md` — Sku aggregate vs value-object alias decision
- `src/Services/Inventory/ShopFlow.Inventory.Infrastructure/EntityConfigurations/SkuConfiguration.cs` — canonical reference; imports both `Domain.Catalog` AND `Domain.Catalog.ValueObjects` explicitly.
