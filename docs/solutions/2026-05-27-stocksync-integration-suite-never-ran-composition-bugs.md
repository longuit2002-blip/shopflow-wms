---
name: stocksync-integration-suite-never-ran-composition-bugs
description: Writing the StockSync noisy-neighbor scale gate (finish-line U3) was the first time the StockSync integration suite — including the "real" StockSyncHappyPathTests — ever ran end-to-end. It surfaced EIGHT pre-existing bugs, all in code that built clean + passed unit tests + passed doc-review. The class of bug (composition-root / migration / config-timing) is only caught by booting the real WAF against a real Postgres. This note catalogs them so the pattern is recognized, not re-discovered.
metadata:
  type: bug
  date: 2026-05-27
  tags: [integration-tests, composition-root, di-lifetime, memory-cache, migrations, webapplicationfactory, stocksync, never-ran, finish-line]
---

# The StockSync integration suite had never run — eight composition bugs

## Context

The finish-line plan (U2/U3) ran the project's "hard-problem proof" integration
tests for the first time on a dev machine now that Docker is available. The
StockSync noisy-neighbor scale gate (`MultiTenantStockSyncScaleGateTests`) was
an empty `[Fact(Skip)] { Task.CompletedTask; }` stub; writing its real body on
top of the SAME harness the (real-bodied, "passing") `StockSyncHappyPathTests`
uses revealed that **neither had ever run end-to-end** — eight distinct bugs sat
between "host boots" and "a stock change reaches the channel adapter," every one
of which builds clean, passes the unit suite, and passed doc-review.

This is the central finish-line thesis in miniature: **green-on-paper is not
green-in-reality.** Per-PR CI runs only `SharedKernel.IntegrationTests` (with a
`FakeTenantCatalog`); every other module's integration tests were `[Fact(Skip)]`
or fixture-fail-on-no-Docker, so the real composition root was never exercised.

## The eight bugs (in the order they surfaced)

1. **Migration referenced a non-existent column.** `StockSyncIndexAudit`
   (Sprint-7.5 U10, judgment-authored without Docker) created an index on
   `stock_sync_push_log (channel_id, occurred_at)` — the table has
   `channel_type` + `observed_at`. `42703` on every migration apply.
2. **Same migration referenced a non-existent table.** Its second index targeted
   `sku_flags`; the table is `stock_sync_sku_flag` (Sprint-2.5 per-module prefix,
   singular). `42P01`.
3. **Test project missing the migrations-assembly reference.** The catalog
   helper migrates `ControlPlaneDbContext` via
   `MigrationsAssembly("ShopFlow.ControlPlane.Migrations")`; that assembly wasn't
   referenced, so it wasn't on the probing path (`FileNotFoundException`).
4. **Minimal-hosting WAF config timing.** `ConfigureAppConfiguration` +
   `AddInMemoryCollection` lands too late for the composition-root reads
   (`AddShopFlowDefaults` KTD7 guard, `AddControlPlane`). `UseSetting` (web-host-
   builder config) is read in time. The happy-path used the late pattern.
5. **`ITokenIssuer` Singleton consuming scoped `IRolePermissionRepository`**
   (Auth; finish-line U2) — see
   [2026-05-27-jwt-token-issuer-singleton-consumes-scoped.md](./2026-05-27-jwt-token-issuer-singleton-consumes-scoped.md).
6. **`CachingSkuFlagRepository` Singleton capturing scoped `ITenantCatalog`.**
   The singleton cache injected `ITenantCatalog` (scoped) in its ctor. It already
   opens a per-call scope — fix: resolve `ITenantCatalog` *inside* that scope.
7. **`TenantCatalog` MemoryCache: `SizeLimit=1000` set, entries had no `Size`.**
   `IMemoryCache.Set` throws "Cache entry must specify a value for Size when
   SizeLimit is set" on EVERY hydrate — breaking every real tenant lookup
   (dispatcher enumeration, consumer SKU-flag reads, routing middleware). Hidden
   because per-PR CI uses `FakeTenantCatalog`. Fix: `Size = 1` per entry.
8. **`AddDbContextFactory<StockSyncDbContext>` defaulted to a Singleton factory**
   whose configure-lambda `sp` is the ROOT provider; resolving the scoped
   `IRequestContext` there threw "Cannot resolve scoped service … from root
   provider" on every consume + flush. Fix: `ServiceLifetime.Scoped` so the
   factory's `sp` is the per-call scope.

(Bugs 5–8 are the same family: **a singleton — explicit or DI-defaulted —
capturing or root-resolving a scoped dependency.** DI scope validation catches
them only when the real graph is built, which `WebApplicationFactory`
(`ValidateOnBuild`) and the Aspire dev host (`ValidateScopes` in Development) do
— but unit tests that `new` the type and per-PR CI that fakes the catalog do
not. Bugs 5, 6, 8 almost certainly explain the live-boot #8 — Auth.Api +
StockSync.Api "not coming up" under `task up`.)

## Lessons

1. **A Skip-marked integration test that runs nowhere is negative coverage.** It
   advertises a guarantee that doesn't exist and lets composition-root rot
   accumulate. The finish-line `ProofGate` (env/CI opt-in) replaces blanket
   `Skip` so these run locally on demand and automatically in CI.
2. **`WebApplicationFactory` boot == composition-root smoke test.** If a service
   won't boot under the WAF (scope validation, config-timing, startup guards),
   it won't boot under `task up`. Boot it in a test.
3. **When you add a scoped dependency, audit the consumer's lifetime** — and
   remember `AddDbContextFactory` / `AddMemoryCache(SizeLimit=…)` have
   non-obvious defaults (singleton factory; mandatory per-entry `Size`).
4. **Judgment-authored migrations need a real apply.** Bugs 1–2 were "defensive
   indexes" written without Docker; they guessed column + table names and were
   wrong. A migration that never applied to a real DB is unverified.
5. **Fakes in CI hide real-implementation bugs.** `FakeTenantCatalog` kept the
   `TenantCatalog` `Size` bug invisible for many sprints. Pair fakes with at
   least one real-implementation integration path.
