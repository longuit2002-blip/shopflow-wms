---
title: "EF Core 9 PendingModelChangesWarning vs hand-authored migrations"
date: 2026-05-13
status: active
tags: [ef-core, migrations, agents.md-3.23, sprint-1-redux]
---

# EF Core 9 PendingModelChangesWarning vs hand-authored migrations

## The trap

EF Core 9 added a new diagnostic, `RelationalEventId.PendingModelChangesWarning`, promoted to **error-by-default**. When `MigrateAsync()` runs, EF compares the runtime model (built from entity configurations) against the **model snapshot file** that lives alongside the migration class. If the snapshot says the model should look like X but the configurations build Y, EF raises this warning.

The trap for our codebase: per [AGENTS.md §3.23](../../AGENTS.md), all module migrations are **hand-authored** with `[Migration]` + `[DbContext]` attributes. The hand-authored pattern was canonised after the v2.0 silent-no-op defect ([docs/solutions/2026-05-10-ef-migration-needs-attributes.md](2026-05-10-ef-migration-needs-attributes.md)). That pattern ships **only the migration class** — it does NOT ship the `<DbContext>ModelSnapshot.cs` file that `dotnet ef migrations add` would emit alongside the migration.

Under EF 9, the missing snapshot means EF assumes the baseline model is empty. So when `MigrateAsync()` runs against our hand-authored `InitialInventorySchema`:

1. EF builds the runtime model from `StockItemConfiguration`, `ReservationConfiguration`, etc. — 5 tables.
2. EF reads the model snapshot — finds nothing → assumes empty baseline.
3. EF compares "5 tables in current model" vs "empty baseline" → 5 tables are "pending changes".
4. `PendingModelChangesWarning` fires → promoted to error → `MigrateAsync()` throws.

Every integration test that calls `MigrateAsync()` fails before any assertion runs. The signal looks like real model drift but is a structural false positive.

## How it surfaced

Sprint-1-redux's `ReservationRepositoryTests` (14 tests against Testcontainers Postgres) all failed on the first Docker-enabled run with:

> `System.InvalidOperationException : An error was generated for warning 'Microsoft.EntityFrameworkCore.Migrations.PendingModelChangesWarning': The model for context 'InventoryDbContext' has pending changes. Add a new migration before updating the database.`

Stack trace pointed at `InventoryTenantFixture.ProvisionTenantAsync` → `Database.MigrateAsync()`. Phase-0-redux U10 and Sprint-1-redux U6 sign-offs both deferred Docker-backed integration runtime measurement — so the issue shipped to main undetected.

`dotnet build` does not catch this (it's a runtime check). `dotnet test --filter "Category!=Integration"` does not catch this (no `MigrateAsync()` call path). The first run that exercises real Postgres trips it immediately.

## The fix

Suppress the warning at the DbContext level. The hand-authored pattern is canonical per AGENTS.md §3.23, so we override `OnConfiguring` to tell EF "we are aware there's no snapshot — don't surface this as an error":

```csharp
protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
{
    base.OnConfiguring(optionsBuilder);
    optionsBuilder.ConfigureWarnings(w =>
        w.Ignore(RelationalEventId.PendingModelChangesWarning)
    );
}
```

Applied to:
- `src/Services/Inventory/ShopFlow.Inventory.Infrastructure/InventoryDbContext.cs`
- `src/ControlPlane/ShopFlow.ControlPlane.Infrastructure/ControlPlaneDbContext.cs`

Every future module DbContext that ships hand-authored migrations must include the same override. The pattern is: **if AGENTS.md §3.23 applies (which it does for every per-tenant module per ADR-0003), the warning must be ignored.**

## Why not generate a snapshot?

The alternative is to run `dotnet ef migrations script` (or equivalent) to produce a real `<DbContext>ModelSnapshot.cs` next to each migration. This would satisfy EF 9's check without suppressing.

Reasons we don't do that here:

1. **AGENTS.md §3.23 codifies the hand-authored discipline.** Adding `dotnet ef`-generated artifacts creates a hybrid pattern where some migrations have snapshots and some don't, which is confusing to read.
2. **Tooling drift risk.** Snapshot files contain a serialised model graph that drifts if you forget to regenerate after editing entity configurations. The hand-authored pattern is intentionally manual — adding a tool-generated companion file reintroduces the drift problem.
3. **Schema correctness is enforced elsewhere.** `MigrationSmokeTests` (per AGENTS.md §3.23) asserts named tables, primary keys, unique indexes, and foreign keys exist after `MigrateAsync()` runs. That is the real check against drift between configuration and migration — far more authoritative than EF's snapshot comparison.
4. **The suppression composes.** If a future engineer DOES generate a real snapshot for a specific DbContext (say, for the picking module that gets generated from scratch in Sprint-3-redux), the suppression is a no-op when the model matches the snapshot — no negative interaction.

## Carry-forward rule

When adding a new module DbContext under the hand-authored migration pattern:

1. Write the migration class with `[Migration]` + `[DbContext]` attributes (per [2026-05-10-ef-migration-needs-attributes.md](2026-05-10-ef-migration-needs-attributes.md)).
2. **Do NOT generate a model snapshot** via `dotnet ef migrations add`.
3. Override `OnConfiguring` in the new DbContext to ignore `RelationalEventId.PendingModelChangesWarning`, citing this learning.
4. Extend `MigrationSmokeTests` with the new DbContext + its named tables / constraints / indexes (the actual drift detector).
5. CI runs the smoke test on every PR, providing the canonical schema-drift signal.
