---
name: aspire-dev-stack-first-boot-repairs
description: The Aspire AppHost dev stack had never been booted on a developer machine; the first live boot (2026-05-27) surfaced a chain of breakages. This note records what blocks `task up` and which are fixed vs still open.
metadata:
  type: reference
  date: 2026-05-27
  tags: [aspire, dev-stack, pgbouncer, migrations, first-boot, sprint-13]
---

# Aspire dev-stack first-boot repairs

The `ShopFlow.AppHost` Aspire orchestrator (`task up`) had never successfully booted on a developer machine before 2026-05-27 (CLAUDE.md repeatedly notes "no local Docker daemon" — integration tests run only in CI, which exercises modules in isolation, not the full AppHost). The first real boot surfaced a cascade of breakages. This note records the chain so the next person doesn't rediscover it, and so the remaining items can be scoped as a deliberate dev-stack repair.

## What works after the fixes in this commit

- All infrastructure containers boot: postgres:16, pgbouncer, redis, rabbitmq, seq, prometheus, tempo, otel-collector, minio, mailpit.
- The Aspire dashboard serves at `http://localhost:17100`.
- The `shopflow-migrate` chain provisions the control-plane catalog + dev tenants end-to-end (verified by driving it directly): `shopflow_control` + `shopflow_t_dev1` + `shopflow_t_dev2`, both tenants `Ready`, with the full Sprint-13 four-role `role_permissions` baseline (34 rows = 24 Owner + 4 Picker + 3 Dispatcher + 3 Packer).

## Breakages found + fix status

| # | Symptom | Root cause | Status |
|---|---|---|---|
| 1 | `dotnet run` AppHost throws `OptionsValidationException: ASPNETCORE_URLS ... ASPIRE_DASHBOARD_OTLP_ENDPOINT_URL ... not set` | No `Properties/launchSettings.json` on the AppHost — the dashboard env vars were never supplied | **FIXED** — added `launchSettings.json` (`http` profile sets applicationUrl + OTLP endpoint + `ASPIRE_ALLOW_UNSECURED_TRANSPORT`) |
| 2 | pgbouncer container never created; `docker pull bitnami/pgbouncer:1.23.1` → `not found` | Bitnami removed legacy image tags from Docker Hub (2025 catalog change); the pinned tag 404s | **FIXED** — image → `bitnamilegacy/pgbouncer:1.23.1` (same version, relocated org) |
| 3 | pgbouncer exits(1): `POSTGRESQL_PASSWORD must be set` | The bitnami(legacy) entrypoint validates `POSTGRESQL_*` upstream env even with a custom bind-mounted `pgbouncer.ini` | **FIXED (partial)** — added `POSTGRESQL_HOST/PORT_NUMBER/USERNAME/PASSWORD`. See #5 below — this makes pgbouncer *start* but triggers auto-config. |
| 4 | `shopflow-migrate` exits: `configuration 'Postgres:AdminConnectionString' is required` | `Host.CreateApplicationBuilder` loads `appsettings.json` from the CWD, but the AppHost launched the migrate executables with `workingDirectory: repoRoot` (no appsettings.json there) | **FIXED** — migrate executables now use `workingDirectory: migrateProjectPath` |
| 5 | Tenant provisioning: `42P01: relation "role_permissions" does not exist` | **Sprint-13 migration ordering bug.** `AddPackerRole` was timestamped `20260527000001`, which sorts BEFORE the Auth schema migration `20260601000001_AddSprint9AuthSchema` (the Auth chain is future-dated to June). So `AddPackerRole`'s `ALTER TABLE role_permissions` ran before that table was created. | **FIXED** — renamed migration to `20260602000001_AddPackerRole` (sorts after the Auth schema). Build + unit tests + doc-review missed this; it only fails when migrations apply in-order to a real DB. |

## Remaining open items (for the dedicated dev-stack repair)

These were NOT fully fixed; they need a deliberate pass:

- **PgBouncer config clobber (extends #3).** With the `POSTGRESQL_*` env set, the bitnami(legacy) entrypoint auto-generates `/opt/bitnami/pgbouncer/conf/pgbouncer.ini` (knowing only the `postgres` database) and runs THAT instead of the bind-mounted `/etc/pgbouncer/pgbouncer.ini` (which has the correct `[databases]` for `shopflow_control` + tenants). Result: connecting to `shopflow_control` through PgBouncer → `08P01 no such database`. Options: switch to `edoburu/pgbouncer` (respects a mounted `pgbouncer.ini`, no auto-config), or find the bitnami flag to disable auto-config.
- **App-role / catalog ordering chicken-and-egg.** `provision --catalog` applies catalog migrations via the ControlPlane connection (PgBouncer, as `shopflow_app`), but `shopflow_app` is only created by `TenantProvisioner.EnsureAppRoleAsync` during `provision --tenant`. So catalog-as-`shopflow_app` has no role to authenticate as on a clean cluster. (Driving the chain succeeds only because the manual repair connected the catalog directly as the `postgres` superuser.)
- **Service HTTP startup (#8, unconfirmed).** After provisioning completed and both tenants were `Ready`, the `Auth.Api` / `StockSync.Api` / `Notification.Api` project resources did not come up as listening HTTP processes within the observed window. Not diagnosed — next step is the Aspire dashboard resource view / per-service crash logs.

## How to actually boot it today (workarounds used during the first boot)

With the committed fixes (#1, #2, #3-partial, #4, #5) plus a local-only Postgres reuse, the stack provisions cleanly by:
1. Relocating the Aspire Postgres host port off a contended 5432 (only needed if a native Postgres already holds 5432), AND
2. Bypassing PgBouncer for migrate + services by pointing their ControlPlane/Tenant connections directly at Postgres as the `postgres` superuser (sidesteps the PgBouncer-config + app-role-ordering items above).

Both of those are local-run workarounds and were intentionally NOT committed — they are config divergences, not architecture fixes. The real fixes are the two "remaining open items" above.

## Lesson for future migrations

Hand-authored EF migration IDs must sort AFTER the latest existing migration **on the same DbContext**, not merely be unique. The Auth chain uses future-dated timestamps (`20260601...`); a new migration dated with the actual calendar day (`20260527...`) silently inserts itself mid-chain and runs against a schema that doesn't yet exist. When adding a migration, check the latest existing ID on that DbContext and exceed it — uniqueness alone is insufficient. (Doc-review's feasibility check verified the timestamp was unused but not that it sorted last; add the ordering check.)
