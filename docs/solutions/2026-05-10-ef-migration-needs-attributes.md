---
title: "Hand-authored EF Core migrations need [Migration] + [DbContext] attributes"
date: 2026-05-10
tags: [ef-core, migrations, postgres, integration-tests]
applies_to: src/Services/Inventory/ShopFlow.Inventory.Infrastructure/Migrations
severity: high
---

# Hand-authored EF Core migrations need `[Migration]` + `[DbContext]` attributes

## Problem

Phase-0 U6 hand-authored a migration class `InitialInventorySchema` (instead of generating with `dotnet ef migrations add`) and it **looked correct** — compiled, passed code review, produced a clean `dotnet build`. But every Inventory integration test failed at runtime with:

```
Npgsql.PostgresException : 42P01: relation "stock_items" does not exist
```

Even though `PostgresFixture.InitializeAsync` calls `MigrateAsync()` against a freshly-started Testcontainers Postgres.

The defect surfaced only when Sprint-1 ran the integration suite for real (Phase-0 sign-off had `Category=Integration` filtered out, hiding it).

## Root cause

EF Core 8 discovers migration classes via two attributes on the partial class:

```csharp
[DbContext(typeof(MyDbContext))]
[Migration("20260427000001_InitialInventorySchema")]
public partial class InitialInventorySchema : Migration { ... }
```

Without **both** attributes:
- `GetPendingMigrationsAsync()` returns an empty list (no migrations registered against the DbContext)
- `MigrateAsync()` is a silent no-op — it returns successfully without applying anything
- The DB stays empty
- The first INSERT against `stock_items` then fails with `42P01`

`dotnet ef migrations add` injects both attributes automatically. **Hand-authored migrations are easy to draft without them** because the file looks complete — `Migration` base class, `Up`/`Down` methods, `MigrationBuilder` calls — and everything builds clean. The model snapshot file (`*ModelSnapshot.cs`) carried `[DbContext(typeof(InventoryDbContext))]` but the migration itself did not.

## Fix

Add both attributes to every hand-authored migration:

```csharp
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using ShopFlow.Inventory.Infrastructure;

[DbContext(typeof(InventoryDbContext))]
[Migration("20260427000001_InitialInventorySchema")]
public partial class InitialInventorySchema : Migration { ... }
```

The `[Migration]` string MUST match the file name's timestamp prefix exactly — that's the migration id EF Core writes to `__EFMigrationsHistory` and uses for ordering.

## Detection

Verify any hand-authored migration with:

```csharp
var pending = await db.Database.GetPendingMigrationsAsync();
// must contain the migration name
```

Or simpler: an integration test that hits a freshly-migrated DB with a real INSERT. The W3 reservation-ledger Sprint-1 caught it; Phase-0 did not because integration tests were filtered out of per-PR.

## Lesson for Phase-2+

- **Prefer `dotnet ef migrations add`** over hand-authoring whenever possible — the tooling injects attributes and rebuilds the model snapshot. If you must hand-author (e.g., when the DbContext design-time factory is missing), copy the attribute pattern from a tool-generated migration in another project.
- **Per-PR CI must run at least one Testcontainers smoke test against the migration**, or this class of defect re-emerges every time we add a module. Today the per-PR filter `Category!=Integration` hides it; consider adding a `Category=Smoke` tier that runs one migration-applies-cleanly check per module per PR.
- **Phase-0 sign-off claim "all gates measured" was incorrect for the migration gate** — that gate was structurally absent. This is captured here so the same blind spot doesn't repeat for Inbound/Outbound/Channel/Analytics migrations when they ship.
