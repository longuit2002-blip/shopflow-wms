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
  - **Update (finish-line U2, 2026-05-27):** the `Auth.Api` half of #8 has a very likely root cause — a DI lifetime bug (`JwtTokenIssuer` registered Singleton while consuming the scoped `IRolePermissionRepository`) that throws at host build under scope validation, which the Aspire dev host enables in Development. Fixed (Singleton→Scoped); see [2026-05-27-jwt-token-issuer-singleton-consumes-scoped.md](./2026-05-27-jwt-token-issuer-singleton-consumes-scoped.md). Surfaced when the Auth WAF booted for the first time. Confirm against a live Aspire boot in the dev-stack repair unit; StockSync/Notification startup still need their own diagnosis.

## How to actually boot it today (workarounds used during the first boot)

With the committed fixes (#1, #2, #3-partial, #4, #5) plus a local-only Postgres reuse, the stack provisions cleanly by:
1. Relocating the Aspire Postgres host port off a contended 5432 (only needed if a native Postgres already holds 5432), AND
2. Bypassing PgBouncer for migrate + services by pointing their ControlPlane/Tenant connections directly at Postgres as the `postgres` superuser (sidesteps the PgBouncer-config + app-role-ordering items above).

Both of those are local-run workarounds and were intentionally NOT committed — they are config divergences, not architecture fixes. The real fixes are the two "remaining open items" above.

## Lesson for future migrations

Hand-authored EF migration IDs must sort AFTER the latest existing migration **on the same DbContext**, not merely be unique. The Auth chain uses future-dated timestamps (`20260601...`); a new migration dated with the actual calendar day (`20260527...`) silently inserts itself mid-chain and runs against a schema that doesn't yet exist. When adding a migration, check the latest existing ID on that DbContext and exceed it — uniqueness alone is insufficient. (Doc-review's feasibility check verified the timestamp was unused but not that it sorted last; add the ordering check.)

## Finish-line U6 progress (2026-05-27)

The finish-line workstream took the dev-stack repair substantially further. Status of the original open items + what's newly fixed:

**Fixed + committed (branch `feat/portfolio-finish-line`):**
- **#8 service startup — RESOLVED for Auth.Api + StockSync.Api.** Both now boot past the composition root (verified standalone in Development, where scope validation is ON). Root causes were composition-root bugs, not infra: `JwtTokenIssuer` Singleton-consuming-scoped (U2), `CachingSkuFlagRepository` + `AddDbContextFactory` lifetime bugs (U3), and `IChannelAdapterFactory` left unregistered in StockSync.Api (U6 — fixed via the new `AddChannelAdapterFramework`). See [2026-05-27-jwt-token-issuer-singleton-consumes-scoped.md](./2026-05-27-jwt-token-issuer-singleton-consumes-scoped.md) + [2026-05-27-stocksync-integration-suite-never-ran-composition-bugs.md](./2026-05-27-stocksync-integration-suite-never-ran-composition-bugs.md). (Notification.Api startup still unverified.)
- **App-role / catalog ordering — RESOLVED.** `shopflow-migrate` now connects DIRECTLY to Postgres as superuser for ALL provisioning (ControlPlane connection was wrongly pointed at PgBouncer). No `shopflow_app`-before-catalog dependency. Verified end-to-end: catalog + dev1 + dev2 provision cleanly direct, 34-row 4-role baseline.
- **Service connection-config rot — RESOLVED.** All four service ControlPlane configs (had drifted to `shopflow`/`postgres`/mixed-port/`{Database}`-token) consolidated to `shopflow_app` via PgBouncer 6432 with the `{db}` token.

**Still open (the orchestrated `task up` to a full explorable floor):**
- **PgBouncer config clobber (K1).** Swap `bitnamilegacy/pgbouncer` → `edoburu/pgbouncer` (reads the mounted `pgbouncer.ini` directly, no auto-config). Needs a pinned edoburu tag + verification of the scram-sha-256 + plaintext-userlist auth against the `shopflow_app` role. Boot-iterative.
- **Native-Postgres-on-5432 coexistence (this machine).** The committed AppHost pins Postgres host port 5432, which collides with the dev machine's native Postgres. Clean fix: a config-driven host port (default 5432 for a clean clone) + inject the resolved Postgres connection into the 3 migrate executables (`WithEnvironment` → `Postgres__AdminConnectionString` etc.), so the dev machine can set a local override (e.g., 5433) to coexist. A clean clone (5432 free) is unaffected. Services use PgBouncer 6432 (a free host port), so only migrate's direct connection is port-coupled.

These two are well-scoped but require live `task up` boot/observe/fix cycles.

## Finish-line U6-finish (2026-06-03)

**Landed (build-verified):**
- **Native-Postgres-on-5432 coexistence — config-driven now.** `ShopFlow.AppHost/Program.cs` reads `DevStack:PostgresHostPort` (default 5432). A clean clone with a free 5432 is unchanged (the proven bootstrap path); a dev machine with a native Postgres on 5432 sets `DevStack__PostgresHostPort=5433` (env or user-secret) and the three `shopflow-migrate` executables receive a matching `Postgres__AdminConnectionString` for their direct-DDL path. The override branch only fires when the port is non-default, so the clean-clone path is byte-for-byte the proven one.

**Documented as the remaining clean-clone/CI dev-stack repair** (this dev machine runs a native Postgres on 5432, so a full `task up` boot to the K6 floor is verified on a clean clone / in CI, not here — per the finish-line brainstorm AE6 "state the prerequisite up front" and K6 "document which and why"):

- **PgBouncer edoburu swap (K1) — still pending live verification.** Swap `bitnamilegacy/pgbouncer` → `edoburu/pgbouncer` (reads the mounted `pgbouncer.ini` directly, no auto-config clobber). The swap itself is a one-line image change, but it needs a live boot to verify the `scram-sha-256` auth + plaintext userlist against the `shopflow_app` role — not landed unverified, because a wrong auth shape would silently break every pooled connection.
- **Inventory.Api is not yet a K6-floor surface.** `Inventory.Api/Program.cs` calls `UseTenantRouting()` but does NOT register `AddControlPlane` (so `ITenantCatalog` is unresolved) and maps no `/health` endpoint. Before it can be the "real authenticated GET an evaluator pokes," it needs: `AddControlPlane(configuration)` + a `/health` map + an AppHost `AddProject<Projects.ShopFlow_Inventory_Api>` resource (mirror `stocksync-api`: `WithReference(postgres)` + ControlPlane env + `WaitForCompletion(migrateDev2)` + `WithExternalHttpEndpoints`). This is composition work that must be boot-verified, not shipped blind.
- **Gateway (YARP) wiring.** The gateway routes to module APIs via `appsettings.json` addresses; wiring it as an Aspire resource with service-discovery to the in-process module resources is its own boot-verified step.

**The headline is done + verified, independent of `task up`:** `task proofs` runs all four hard-problem proofs **and** the multi-channel sync proof green locally on this machine (Docker live) — oversell scale gate, noisy-neighbor, cross-tenant isolation, cross-role RBAC (+ 4-role hand-off), and Shopee+Lazada fan-out. The proofs are Testcontainers-based and fully decoupled from the Aspire `task up` stack, which is why the brainstorm scoped `task up` to "boots enough to explore" with documented prerequisites rather than a hard gate.

## Live `task up` → real backend login (2026-06-07)

Booted the full Aspire stack on this dev machine (native Postgres lives on 5432) and drove it to a **working real-backend login through the gateway** — `POST /api/auth/login` returns a full session (access + refresh) for a seeded Owner. The two `still pending` items above (PgBouncer edoburu swap + migrate targeting) are now **RESOLVED + committed**, plus a previously-undetected template bug that the bitnami clobber had been masking.

**Fixed + committed:**

- **PgBouncer edoburu swap (K1) — RESOLVED + live-verified.** `bitnamilegacy/pgbouncer` → `edoburu/pgbouncer:v1.25.1-p0` (pinned; `v`-prefixed tag — the brace-free `1.23.1-p2` style does NOT exist on Docker Hub). Mounted **read-write** (the edoburu entrypoint chowns `/etc/pgbouncer` on start), all `PGBOUNCER_*`/`POSTGRESQL_*` env dropped. edoburu runs vanilla PgBouncer against the bind-mounted `pgbouncer.ini` as-is when no `DB_HOST`/`DATABASE_URL` is set — so the rendered `[databases]` (control-plane + tenants) + `userlist.txt` (`scram-sha-256` + plaintext `shopflow_app`) are what it actually loads. Services reach the catalog + tenant DBs through 6432 again; the `08P01: no such database: shopflow_control` is gone.

- **`pgbouncer.ini.template` comment-token corruption — NEW bug, the real reason edoburu first crashed.** The template's documentation comment listed the literal `{databases}`/`{auth_file}`/`{admin_users}` tokens. `PgBouncerConfig.Render` does a plain `string.Replace` across the **whole** file, so the multi-line `[databases]` block got spliced into the comment region — the first DB line stayed commented (`;`-prefixed) but `shopflow_t_dev1`/`dev2` landed as bare lines with no section header. Vanilla PgBouncer rejects this (`ERROR load_init_file: value without section: shopflow_t_dev1` → `FATAL cannot load config file`). **bitnami never surfaced this because it ignored the mounted file and auto-generated its own** `postgres`-only config — the clobber was hiding a genuinely malformed render. Fix: rephrased the template comment to be brace-free (the only tokens left are the real body placeholders). This also fixes the **prod handoff**, which renders the same template. Lesson: a token used as both documentation and a substitution target is a footgun for whole-file `Replace`; keep doc references brace-free or substitute only within sections.

- **Migrate chain must follow the relocated port for ALL THREE connections (extends the U6-finish coexistence fix).** The U6-finish fix overrode only `Postgres__AdminConnectionString` when `DevStack:PostgresHostPort != 5432`. But `ControlPlane__ConnectionString` + `ControlPlane__TenantTemplate` still came from migrate's appsettings (`localhost:5432`), so on a native-Postgres-on-5432 machine the **catalog + tenant migrations applied against the native Postgres** while the services read the Aspire container via PgBouncer — two different clusters, empty catalog, login 500. Fix: `AppHost/Program.cs` now overrides all three (Admin + ControlPlane + TenantTemplate) to the relocated direct-superuser port inside the `pgHostPort != 5432` branch. `launchSettings.json` pins `DevStack__PostgresHostPort=5433` for this machine (DCP proxies 5433 → the container; clean clones with a free 5432 are unaffected).

**Confirmed working end-to-end:** `dotnet run` AppHost → infra + edoburu(6432) + Postgres(5433) + migrate chain (idempotent against the persisted `shopflow-postgres-data` volume) → gateway(8080) → `POST /api/auth/login {tenantSlug:"dev1"}` → **full session**. The web dev server (Vite 5173) proxies `/api/*` to the gateway, so the SPA logs into the real backend.

**Residual / notes:**
- The seeded Owner (`owner@<slug>.local`) defaults to `mfa_required=true` (R17). For a frictionless dev login the dev1 Owner was set `mfa_required=false` directly; this lives in the data volume, so it survives restarts but resets on a volume wipe (a fresh provision re-seeds MFA-on + a random password echoed to the `migrate-dev1` resource log). A durable known-credential + MFA-off path would need an explicit `--owner-password` on the AppHost migrate args plus a dev-only MFA-off step — deliberately NOT added to avoid baking an auth-posture change into the orchestrator.
- DCP does not honor the requested fixed host port for the Postgres **container** mapping (it allocates a dynamic docker port), but it DOES listen the DCP proxy on the requested port (5433/6432) — which is what host-process migrate + services connect through. So `localhost:5433`/`localhost:6432` are correct even though `docker ps` shows a dynamic `->5432`/`->6432` mapping.
- Killing the AppHost (vs Ctrl-C) orphans the DCP-managed containers; remove them before the next boot or DCP collides on the volume/ports. The named volume persists across `rm` (no `-v`).
