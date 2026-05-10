# ADR-0003: Database-per-tenant on shared Postgres cluster, anchored to PDPA SEA compliance

- **Status**: Accepted
- **Date**: 2026-05-11
- **Deciders**: solo dev (longuit2002-blip)
- **Supersedes**: the multi-tenancy stance in `02-technical-design-document.md.docx` v2.0 §4 (RLS-on-shared-DB)
- **Superseded by**: —

---

## Context

`02-technical-design-document.md.docx` v2.0 §4 chose **row-level tenancy with RLS** as the multi-tenancy model and characterized it as "the cheapest scale decision in the whole design." That decision was made before compliance entered the working model. Phase-0 implemented faithfully against it: composite PKs `(tenant_id, sku)` / `(tenant_id, id)` on every table, Postgres RLS policies, `SET LOCAL app.tenant_id = '...'` per request via EF Core interceptor, kernel `TenancyInterceptor` stamping `tenant_id` on writes, EF global query filters, Roslyn analyzers ShopFlow0001-0004 with several rules specific to RLS-era tenant guards.

In May 2026, while running Phase-1 Sprint-1 integration tests for the first time on a Docker-capable host, three findings landed in the same hour:

1. The hand-authored EF migration class was missing `[Migration]` + `[DbContext]` attributes and silently no-op'd for the entire Phase-0 (captured in `docs/solutions/2026-05-10-ef-migration-needs-attributes.md`).
2. Under SERIALIZABLE isolation the conditional-CTE INSERT throws `Postgres 40001 (could not serialize access)` errors that the repository did not catch, breaking the W3 scale gate's premise.
3. The user revisited the multi-tenancy decision with a compliance lens (PDPA Vietnam Decree 13/2023, Singapore PDPA) and concluded that **physical tenant isolation is required for SEA-region compliance**. Auditors increasingly expect to see two databases, two backup lineages, two `DROP DATABASE` blast radii — RLS is a logical guarantee defensible to engineers but harder to defend to compliance auditors who do not read PostgreSQL source code.

Finding (3) makes findings (1) and (2) moot in the original shape — every entity, migration, query filter, and analyzer rule was shaped by the RLS assumption. Patching is more expensive and less honest than restarting.

The redesign was scoped through interactive dialogue captured in `docs/plans/2026-05-11-001-redesign-multi-tenancy-db-per-tenant-plan.md`. Three anchor decisions:

- **Tenancy model**: 1 logical Postgres DATABASE per tenant on a shared cluster.
- **Compliance anchor**: PDPA Vietnam + Singapore PDPA. SOC2 / ISO 27001 are explicit non-goals at this stage.
- **Routing**: per-request — tenant resolved from HTTP context, DbContext factory selects the connection per request scope.
- **Scale anchor**: SaaS-credible — 25-50 validated production-ready tenants on one cluster.

---

## Decision

**Adopt database-per-tenant on a shared Postgres cluster as the multi-tenancy model. Each tenant maps to one logical Postgres DATABASE; all tenants on one regional cluster (Phase-1/2); a separate `shopflow_control` database holds the tenant catalog. Per-request routing via ASP.NET Core middleware that resolves tenant identity, looks up the connection string from the catalog (cached), and scopes the request's DbContext factory accordingly. PgBouncer in transaction-pooling mode is non-optional infrastructure from Phase-0-redux.**

The technical design document v3.0 (`docs/redesign/02-technical-design-document.md`) carries the full mechanism detail. This ADR captures the decision and the rejection of alternatives.

---

## Alternatives considered

### A. RLS on shared DB (the v2.0 stance)

**Rejected.** Logical separation only.

Specific concerns under PDPA scrutiny:
- Auditor pushback: "show me the data segregation". The answer "we have RLS policies and a tested CI assertion" is technically defensible but operationally weaker than "two different connection strings, two different backups".
- SQL injection in one tenant's code path can in principle reach another tenant's data; the application user has access to all rows, RLS is a filter not a permission.
- Application-bug `WHERE` clause omission silently leaks. The CI assertion catches the obvious case but not all paths.
- Backup lineage is one big lineage; PITR for one tenant is operationally awkward.
- Right-to-erasure becomes a `DELETE` with cascade across N tables, with the always-uncomfortable "did I get them all" question. Verifiability is weak.

### B. Schema-per-tenant on shared DB

**Rejected.**

Same compliance posture as RLS — the auditor sees one DATABASE, one backup, one connection lineage. Compliance value is approximately equivalent to RLS, which means the operational cost of N schemas pays for nothing.

Operational concerns:
- Migrations apply N times, serially or with ad-hoc parallelism. At 25-50 schemas this is minutes-to-hours per schema change.
- `pg_catalog` bloats with hundreds of schemas. Connection pooling fragments because Npgsql's pool key is per (connection-string, schema search-path).
- Operational tools (`pg_dump`, replication) operate on databases, not schemas — multi-schema workflows require custom tooling.

### C. Cluster-per-tenant (one Postgres instance per tenant)

**Rejected for Phase-1/2 portfolio scope.**

Highest isolation. Operational cost is enterprise-tier:
- Per-tenant container/instance overhead (memory, CPU baseline).
- Per-tenant monitoring registration.
- Per-tenant backup orchestration.
- Per-tenant capacity planning.

Reserved as a Phase-3+ option for explicit enterprise customers paying enterprise prices. The DB-per-tenant decision keeps the seam (catalog `db_connection` string) flexible enough to mix DB-on-shared-cluster and DB-on-dedicated-cluster tenants without code changes.

### D. Tier-based hybrid (RLS for free tier + DB-per-tenant for paid)

**Rejected.**

Dual mental model. Two code paths for tenant context. Two test surfaces. Auditor scope is "all customer data" — the compliance posture is the weakest of the two paths, which means RLS dominates the audit story regardless of how many paid customers use DB-per-tenant.

The simplification value of "every tenant is in its own database, full stop" outweighs any cost savings from RLS-tier free customers.

### E. Different storage primitive (Cosmos DB partition key, DynamoDB tenant key, etc.)

**Rejected as a compliance-driven choice.**

The compliance question is about isolation, not about the storage engine. Cosmos partition key is logical isolation, equivalent in audit posture to RLS. Migrating to a different storage engine for compliance reasons would introduce a much larger architectural change without solving the compliance question better than DB-per-tenant on Postgres.

The decision to use Postgres for the data plane is independent of this ADR (made implicitly in v2.0 and unchanged); within Postgres, DB-per-tenant is the right tier.

---

## Consequences

### Positive

- **Clean PDPA compliance story.** The data segregation answer is "two databases", verifiable in 30 seconds at the SQL prompt.
- **Right-to-erasure is `DROP DATABASE`** (after retention window). Verifiable, fast, no orphan rows.
- **Per-tenant backup lineage.** PITR for one tenant doesn't touch others.
- **Per-tenant migration staging.** Risky schema changes can roll out tenant-by-tenant.
- **Noisy-neighbor mitigation is straightforward.** PgBouncer caps `max_db_connections` per tenant DB.
- **Operational simplicity per tenant.** Disable a tenant by revoking their DB user's CONNECT privilege.
- **Code is simpler.** No `tenant_id` columns, no composite PKs, no RLS policies, no global query filters, no `TenancyInterceptor`. Tenant correctness lives in the routing layer above, not in every entity.

### Negative

- **PgBouncer is non-optional.** N app instances × M tenant DBs without connection pooling exhausts Postgres. PgBouncer adds an infrastructure component, configuration to maintain, and an HA story for Phase-2.
- **Provisioning automation is required.** Manual `CREATE DATABASE` per tenant doesn't scale past a handful. `shopflow-migrate` CLI is a Phase-0-redux deliverable.
- **Catalog database is a coordinator.** It's a small one (no business data, no PII, just tenant metadata) but it is a central piece. Loss of catalog availability blocks new traffic. HA story for Phase-2.
- **`pg_catalog` ceiling.** Postgres practical limit is ~500-1000 DBs per instance before catalog overhead becomes meaningful. At 50-tenant target, 10-20× headroom; at 500+ tenants, sharding required (Phase-3+).
- **`CREATE DATABASE` cannot run through PgBouncer.** Provisioning bypasses PgBouncer with a direct admin connection. One more knob to maintain.
- **Phase-0 work is being thrown away.** The Phase-0 implementation (kernel TenancyInterceptor, RLS migrations, RLS-shaped analyzers, RLS-shaped tests) is archived. Cost: ~2 weeks of Phase-0 work + 1 week of Sprint-1 work that was in flight. This is the real cost of the late-discovered driver.

### Compliance positioning unlocked

The architecture supports (but does not yet implement) follow-up compliance frameworks:
- **SOC2 Type 2** — would require operational controls (change management, access reviews, incident response runbooks, observation period) but the architectural foundation is consistent. Phase-3+ work.
- **ISO 27001** — same.
- **Cross-region residency** — the catalog `region` field is the seam. Phase-3+ work.
- **Customer BYOK encryption** — per-tenant DB makes per-tenant key management tractable. Phase-3+ enterprise tier.

---

## Verification

The decision is verified against three concrete artifacts:

1. **Phase-1 scale gate** (per Sprint-1-redux plan): 5 tenants × 1,000 concurrent reservations each. Per-tenant fairness floor ≥ 0.85. Cross-tenant data leak count = 0. p99 < 200ms per tenant.
2. **Routing correctness integration test** (per Phase-0-redux plan U8-redux): every endpoint asserts that providing a tenant-mismatched header/JWT/subdomain returns 403/404, never another tenant's data.
3. **Provisioning workflow round-trip**: `shopflow-migrate provision --tenant=test1` → `shopflow-migrate apply --target=latest --concurrency=4` (against multiple tenants) → `shopflow-migrate archive --tenant=test1` → DROP. Catalog state consistent throughout.

The decision is **falsifiable**: if a future load test under unforeseen contention surfaces a per-tenant fairness violation that PgBouncer per-database limits cannot fix, OR if a real PDPA audit raises an objection that DB-per-tenant does not satisfy, this ADR will need a follow-up.

---

## References

- Redesign trigger plan: `docs/plans/2026-05-11-001-redesign-multi-tenancy-db-per-tenant-plan.md`
- Product plan v3.0: `docs/redesign/01-product-development-plan.md` §3 (tenant model), §4 (compliance scope)
- Tech design v3.0: `docs/redesign/02-technical-design-document.md` §1 (multi-tenancy), §2 (provisioning)
- ADR-0001 postscript (this date): Aspire AppHost dev mode adds PgBouncer + control-plane DB + 2 dev tenants
- ADR-0002 postscript (this date): module split timing unchanged; "RLS-as-cheapest-decision" claim superseded
- Postgres documentation, "Concurrency Control" chapter (READ COMMITTED for conditional INSERT pattern)
- PDPA Vietnam: Decree 13/2023/ND-CP — Personal Data Protection
- PDPA Singapore: Personal Data Protection Act 2012 (revised 2020)
