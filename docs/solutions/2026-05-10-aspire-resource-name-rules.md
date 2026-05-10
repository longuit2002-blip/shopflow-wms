# Aspire resource names: ASCII letters / digits / hyphens only — no underscores

**Date**: 2026-05-10
**Affects**: `src/AppHost/ShopFlow.AppHost/Program.cs` and any future Aspire resource registration

## Problem

The U9 AppHost compiled until adding a Postgres database resource:

```csharp
var postgres = builder
    .AddPostgres("postgres")
    .WithDataVolume("shopflow-postgres-data")
    .AddDatabase("shopflow_dev");
```

Build failed with:

```
error ASPIRE006: Resource name 'shopflow_dev' is invalid. Name must contain
only ASCII letters, digits, and hyphens. (https://aka.ms/aspire/diagnostics/ASPIRE006)
```

Underscores in Aspire resource names are rejected by analyzer ASPIRE006.

## Root cause

Aspire 13.x ships analyzer rule `ASPIRE006` that validates resource names against a strict regex: `^[a-zA-Z][a-zA-Z0-9-]*$`. Underscores break the rule. The analyzer fires at compile time so the failure is loud and immediate (good), but it surprises developers who instinctively use snake_case for database names (matching Postgres / MySQL conventions).

Two distinct names are at play:

1. **Aspire resource handle** — used in code (`builder.AddDatabase("foo")`), in the Aspire dashboard, and as the implicit DNS name in service discovery. Aspire validates this with ASPIRE006.
2. **Underlying DB name inside Postgres** — passed via the connection string. Postgres permits underscores; in fact, Postgres conventionally lowercases unquoted identifiers and underscores are common.

## Solution

Use hyphens for the Aspire resource handle; keep underscores in the actual DB name (passed via connection string env vars):

```csharp
var postgres = builder
    .AddPostgres("postgres")
    .WithDataVolume("shopflow-postgres-data")
    .AddDatabase("shopflow-dev");        // Aspire handle: hyphens

// Later, the connection string can still carry the underscore form:
var inventoryApi = builder.AddProject<Projects.ShopFlow_Inventory_Api>("inventory-api")
    .WithEnvironment("ConnectionStrings__Inventory",
        "Host=postgres;Port=5432;Database=shopflow_dev;Username=postgres;Password=postgres");
//                                       ^^^^^^^^^^^^ — underscore is fine here
```

## Prevention

1. **Aspire resource handles use hyphens.** When picking the name, ask: is this a string Aspire owns, or a string a downstream system owns? Aspire owns resource names + service discovery; downstream owns DB names, RabbitMQ vhosts, Redis keys, etc.
2. **The other ASPIRE000–ASPIRE020 rules are worth a one-time scan** when adding any new resource type. They tend to be loud and informative, but knowing the rule before writing the code saves the round-trip.
3. **Aspire generates `Projects.ShopFlow_Inventory_Api` from project file names** — the underscores there come from the project's csproj filename. Not the same name space as Aspire resource handles. The `IsAspireProjectResource="true"` ProjectReference plus the project source generator handle this; you don't pick that name.

## References

- `src/AppHost/ShopFlow.AppHost/Program.cs` — line 29 (AddDatabase) + line 100 (connection string with underscore intact)
- Aspire diagnostics: https://aka.ms/aspire/diagnostics/ASPIRE006
- See also: [`2026-05-10-aspire-adddockerfile-context-path.md`](2026-05-10-aspire-adddockerfile-context-path.md) — the other Aspire-specific gotcha from U9
