# ShopFlow WMS — Product Development Plan

**Multi-Channel Warehouse Management System for SEA Marketplaces, with hard tenancy isolation under PDPA**

- **Version**: 3.0 (redesign — see `docs/CHANGELOG.md` and `docs/plans/2026-05-11-001-redesign-multi-tenancy-db-per-tenant-plan.md`)
- **Last Updated**: 2026-05-11
- **Scope**: 12-week portfolio build, designed to hold up under SaaS-credible scale and SEA-region compliance scrutiny
- **Companion doc**: `02-technical-design-document.md` (architecture, scale, SLO design, multi-tenancy mechanics)
- **Supersedes**: `01-product-development-plan.md` v2.0 (April 2026), which assumed RLS-shared tenancy and individual-seller persona.

---

## 1. Framing

### 1.1 What this document is

A product plan for a 12-week single-developer build. The deliverable is a portfolio-grade system that a senior engineer can pull apart and see real engineering judgment: honest scope, realistic scale reasoning, tradeoffs called out, and deferred work named explicitly.

The system is built at MVP scope (5 production-ready tenants, single Postgres cluster, mocked channel APIs), but designed so the path to **25-50 validated tenants under noisy-neighbor load** is concrete and the compromises beyond that — sharded clusters, cross-region residency — are explicit. Every significant technical decision in the companion design doc cites the scale at which it holds up and the scale at which it breaks.

This revision (v3.0) re-grounds the plan around a compliance-driven hard isolation model. The trigger is named in the redesign plan: a Phase-1 Sprint-1 finding that physical tenant isolation is required for SEA-region PDPA compliance, which RLS-on-shared-DB cannot honestly deliver.

### 1.2 What we are building

A multi-channel inventory and fulfillment control plane for **registered SME businesses** operating across Shopee, Lazada, TikTok Shop, and Shopify. One warehouse, one source of truth for stock, event-driven sync to every connected marketplace, and operational tooling for the receive → put-away → pick → pack → ship loop — delivered as a multi-tenant SaaS where every tenant's data sits in its own physically isolated database.

### 1.3 What we are not building

Named explicitly so the scope does not drift. See §10 for rationale.

- **No fleet-of-warehouses topology.** Single warehouse with a zone/bin layout per tenant.
- **No real marketplace API integrations.** Mock servers reproduce Shopee's and Lazada's wire protocol (idempotency keys, webhook signatures, rate limits, error codes). Swapping to real APIs is an adapter change.
- **No demand forecasting, replenishment recommendations, or ML.**
- **No carrier API integrations.** Mocked shipping label service.
- **No mobile app.** Web frontend is responsive enough for warehouse floor tablets but is not a PWA.
- **No accounting, invoicing, or VN tax compliance** (e-invoicing, 10% VAT). Documented as a scale-tier dependency.
- **No 3PL or dropshipper workflows.**
- **No SOC2 / ISO 27001 controls.** PDPA SEA is the compliance anchor at this stage; SOC2 is a follow-up roadmap item, not part of the architectural commitment.
- **No cross-region tenant residency.** All tenants in the same regional cluster; cross-region failover is Phase-3+ work.
- **No tier-based hybrid tenancy** (e.g., RLS for free tier + DB-per-tenant for paid). Every tenant is in its own database, full stop. This was explicitly rejected at redesign time as a dual-mental-model burden.

---

## 2. Problem

### 2.1 Why sellers lose money today

A VN SME selling on three marketplaces typically runs inventory in a spreadsheet and updates each platform manually. At ~500 orders/day this produces measurable dollar cost:

- **Oversell loss.** Sync lag between marketplaces causes oversell. Shopee penalises cancellation: shop rating drops, search ranking drops, repeated offenses cap order volume. Observed oversell rates among sellers using manual sync sit in the 1–3% range; each oversold order costs roughly 2–5× the margin (fulfillment cost + penalty + reputational opportunity cost).
- **Ops labor.** A single operator updating stock across 3 marketplaces spends 1–2 hours/day on sync alone. At 5,000 SKUs this rises to full-time headcount.
- **Stockout blindness.** Without per-channel allocation, a seller routinely stocks out on one marketplace while sitting on inventory in another, missing revenue.
- **Fulfillment errors.** Manual pick lists produce 2–5% pick errors at scale; returns shipping is borne by the seller.

The sellable wedge is therefore not "inventory tracking" — that's what competitors do adequately. It's **bounded sync latency with correctness guarantees at flash-sale load**, delivered with **tenancy isolation strong enough to satisfy PDPA scrutiny**, which is where existing tools in this market visibly compromise.

### 2.2 Competitive positioning

| Competitor | Strength | Gap we exploit |
|---|---|---|
| Sapo Omnichannel | VN market brand, POS integration | Sync lag visible during campaigns; opaque oversell handling; shared-DB tenancy |
| Haravan | Strong retail features, VN tax support | Older architecture, weaker real-time sync; tenancy posture undocumented |
| KiotViet | POS-first, retail-heavy | Marketplace sync is an add-on; not designed for multi-channel-first sellers |
| Omisell / Anchanto | Regional multi-channel focus | Pricing targets larger sellers; overkill for 1–10K SKU SMEs |
| In-house spreadsheets | Free | Manual; breaks above ~1K SKUs or 3 channels; no compliance story |

ShopFlow's positioning: **event-driven sync with sub-30s p99 latency under flash-sale burst, delivered with database-per-tenant hard isolation that reads cleanly under PDPA audit, priced for SMEs**, with operator tooling that matches the actual warehouse workflow rather than imposing ERP vocabulary. For a portfolio build, we are not trying to beat competitors on feature breadth — we are demonstrating the engineering that multi-channel sync at scale **and** SaaS multi-tenancy in a compliance-sensitive region actually requires.

---

## 3. Tenant Model and Target Users

### 3.1 What is a tenant

A **tenant** in ShopFlow is a **registered legal entity**: a company with a verifiable business registration number (Vietnam: ERC / GPKD; Singapore: UEN; Indonesia: NIB). The smallest possible tenant is a **sole proprietorship** with at least one verified business registration. Individual sellers without a registered business are explicitly out of scope at this stage — onboarding such a tenant would create unclear PDPA processor/controller boundaries that the architecture does not attempt to handle.

Each tenant maps to exactly one logical Postgres database, isolated at the connection level. The tenant identity is the `tenant.slug` (immutable after provisioning) which derives the `db_name`. This is documented in the technical design doc §1 (multi-tenancy) and §2 (provisioning).

### 3.2 Personas inside a tenant

| Persona | Volume profile | Core pain the system solves |
|---|---|---|
| **SME Seller (operator-owner)** — typical sole-proprietorship CEO | 1–5K SKUs, 2–5 channels, 100–1K orders/day | Oversell prevention, single-screen stock truth, one-click channel reconnection, audit-ready data isolation for marketplace partner reviews |
| **Warehouse Operator** | Executes inbound/outbound on warehouse floor | Zero-ambiguity pick lists (zone + bin), error-proof packing checks, minimal typing |
| **Operations Manager** | Oversees warehouse KPIs | Fulfillment SLA visibility, SLA breach alerts, channel performance by revenue and margin |

The persona matrix is unchanged from v2.0 except for the operator-owner row, which gains "audit-ready data isolation" as a now-explicit pain. Sole proprietorships in Vietnam undergoing partnership review with Shopee Mall or Lazada Mall increasingly face questions about how tenant data is segregated; ShopFlow's hard isolation is the answer.

### 3.3 Tenancy is the foundation, not a phase

Tenancy is **not deferred** in this revision. The data plane is database-per-tenant from Phase-0. The control-plane catalog is shipped in Phase-0. Provisioning automation is shipped in Phase-0. There is no "single-tenant MVP" stage — the smallest deployment is the control plane plus at least one tenant database, and the dev-mode Aspire AppHost provisions two dev tenants on startup precisely to keep the multi-tenant code paths exercised every working day.

---

## 4. Compliance Scope

The compliance anchor is **PDPA SEA** — specifically Vietnam's Decree 13/2023/ND-CP on Personal Data Protection ("PDPA Vietnam") and Singapore's PDPA. Indonesia's PDP Law follows a similar frame; the architecture is designed to satisfy it without re-work, but Indonesian tenants are not explicitly on the Phase-1 onboarding path.

### 4.1 Obligations the architecture must satisfy

| Obligation | Where in the architecture it lands |
|---|---|
| **Hard data isolation between processors and controllers** | Database-per-tenant. Each tenant's connection string is unique; an SQL injection in one tenant DB cannot reach another. See Tech Design §1. |
| **Data residency in-region** | All Phase-1 tenants land on the same regional cluster (default: Singapore zone). Cross-region failover is Phase-3+. See Tech Design §1.4. |
| **Breach notification within 72 hours** | Control-plane catalog records `breach_notified_at` per affected tenant; observability fires SLA alerts at 24h, 48h, 60h marks. See Tech Design §7.4. |
| **Right to erasure (RTBF)** | Tenant lifecycle includes `archiving → archived` states; the `shopflow-migrate archive --tenant=<slug>` command issues `DROP DATABASE` after a configurable retention window (default 30 days). The clean separation makes erasure cheap and verifiable. See Tech Design §2.3. |
| **Consent management for end-customer data** | Order ingestion stores customer PII (name, address, phone) only in the tenant DB. Per-tenant configuration gates whether customer PII is retained beyond the order fulfillment window. See Tech Design §6. |
| **Sub-processor disclosure** | Tenant catalog records `sub_processors` (Postgres, Redis, RabbitMQ, observability stack); a tenant-facing endpoint exposes the current list. New sub-processors require migration + tenant notification. See Tech Design §1.6. |
| **Audit log retention** | Every business write produces an outbox row; outbox rows in tenant DB are retained for 12 months minimum, then archived. Audit log queries scoped per-tenant. See Tech Design §5. |

### 4.2 What is explicitly NOT covered

- **SOC2 Type 2** — would require a 6-12 month observation period and formal control framework. Out of scope for the Phase-1 portfolio build; named as a Phase-3+ enterprise-tier follow-up.
- **ISO 27001** — same reasoning.
- **GDPR-specific controls** (Right to Data Portability in machine-readable format, automated decision-making transparency) — out of scope until EU customers appear on the roadmap.
- **HIPAA / financial regulations** — out of domain.

The architecture does not preclude these; it does not yet satisfy them. The SOC2 path specifically would benefit from the database-per-tenant foundation (clean access boundaries, simple per-tenant audit), but the controls work — change management, access reviews, vendor risk management, incident response runbooks — is product/operational, not architectural.

### 4.3 Compliance positioning vs. competitors

Competitor compliance posture in SEA is largely undocumented. Sapo, Haravan, and KiotViet operate on shared infrastructure with logical separation; none publish a clear PDPA Article 9 (data security) statement. ShopFlow's position: **the per-tenant database is the data security statement**. Auditor questions like "how do you guarantee data segregation?" answer with "two different connection strings, two different DROP DATABASE blast radii, demonstrable in 30 seconds at the SQL prompt".

This is also the reason ADR-0003 (database-per-tenant) was accepted over RLS at redesign time — RLS is a logical guarantee, defensible to engineers but harder to defend to auditors.

---

## 5. Success Metrics

Metrics split into business outcomes (what the seller experiences), technical SLOs (what the system guarantees), and **compliance metrics** (what the auditor verifies).

### 5.1 Business metrics

| Metric | MVP target | Mid-market target (50 tenants validated) | How measured |
|---|---|---|---|
| Oversell rate | < 0.1% of orders | < 0.05% of orders | Count of orders where requested > available_at_accept_time / total orders, 7-day rolling |
| Manual sync hours saved per seller per week | > 10h | > 10h | Seller survey + activity log derived |
| Pick accuracy | > 99.5% | > 99.8% | Confirmed correct picks / total picks, per pick-wave |
| Order-to-ready-for-ship p95 | < 2h | < 2h | Timestamp diff on completed orders |

### 5.2 Technical SLOs

Error budget is derived from the availability target. 99.5% availability ≈ 3.6 hours/month of budgeted downtime; 99.9% ≈ 43 minutes. These budgets gate feature releases — if we burn 80% of the budget in a rolling 30-day window, release freeze.

| SLO | Target | Why this number |
|---|---|---|
| API availability (user-facing) | 99.5% | Matches SME expectations without demanding 24/7 pager |
| Order ingest latency (webhook → persisted) p99 | < 500 ms | Marketplaces retry webhooks; slow ACK = more duplicate work |
| Stock sync latency (change → marketplace ack) p99 | < 30 s | Below this, oversell window is empirically negligible |
| Stock sync latency p50 | < 5 s | Typical case expectation |
| Fulfillment saga completion p99 (Reserve → Ship) | < 5 min | Covers normal warehouse throughput, excludes manual steps |
| Oversell event rate | < 0.05% of accepted orders | Ties to business metric |
| Webhook idempotency correctness | 100% | Non-negotiable; duplicate processing would corrupt stock |
| **Tenant routing correctness** (request → correct DB) | 100% | Non-negotiable; routing leak = compliance breach |
| **Per-tenant fairness floor under noisy neighbor** (worst-case tenant success rate ÷ best-case tenant success rate) | ≥ 0.85 | A flash-sale on tenant A must not collapse tenant B's dashboard. See §5.4. |

### 5.3 Compliance metrics

| Metric | Target | How measured |
|---|---|---|
| **Cross-tenant data leak incidents** | 0 | Routing correctness audit + RLS-equivalent integration test (does tenant A's connection refuse to read tenant B's tables under all paths) |
| **Tenant provisioning time (catalog row → ready)** p99 | < 60 s | Provisioning workflow timestamp diff |
| **Tenant archival time (`archive` command → DROP DATABASE)** | configurable retention window (default 30 days) | Catalog state transition + verifiable DROP |
| **Sub-processor list freshness** | within 7 days of vendor change | Catalog `sub_processors` field + change log review |
| **Breach notification SLA observance** | 100% within 72h | Observability alerts at 24h/48h/60h; manual breach drills quarterly |

### 5.4 Scale targets the design must hold up to

The system is built at MVP scale (5 production-ready tenants) but the architecture is defensible up to **25-50 validated tenants under noisy-neighbor load**. The technical design doc §1 defines the tenancy model, §3 defines the SLOs, §4 walks the reservation ledger at this scale, and §8 names what breaks and what changes at each next tier.

| Dimension | MVP (Phase-1 close) | Mid-market (Phase-2 close) | Out-of-scope (Phase-3+) |
|---|---|---|---|
| Tenants on a single Postgres cluster | 5 | 25–50 | 100+ requires sharding |
| SKUs per tenant | ≤ 5,000 | ≤ 50,000 | unbounded with denormalization |
| Channels per tenant | ≤ 3 | ≤ 6 | ≤ 6 |
| Sustained orders/second per tenant | < 5 | ~30 | ~600 (per shard) |
| Peak flash-sale orders/second per tenant | ~50 | ~500 | ~30,000 (11.11, 12.12, 3.3) |
| Stock changes/second per tenant | < 20 | ~100 | ~2,000 |
| Outbound webhook fan-out per stock change | ≤ 6 channels | ≤ 6 channels | ≤ 6 channels |
| Concurrent tenants under noisy-neighbor load test | n/a | 5 (per scale gate) | 25+ for tier-3 sign-off |

The **noisy-neighbor scenario is non-negotiable from Phase-2**: a flash sale on tenant A (5,000 concurrent reservations) must not push tenant B's p99 above 250ms. The Phase-1 Sprint-1 reservation-ledger scale gate (per Plan §8.3 and the corresponding implementation plan) is now multi-tenant-shaped: 5 tenants × 1,000 concurrent each, with a per-tenant fairness floor assertion.

The delta from MVP to mid-market is roughly **10× tenants × ~10× peak/tenant = 100× total peak**. The companion design doc does not hand-wave through this — each critical path (inventory reservation, stock sync, webhook ingest, fulfillment saga) has an explicit scale analysis under the database-per-tenant model.

---

## 6. Non-Functional Requirements

These are hard constraints. They shape the architecture before a single feature is built.

- **Correctness.** Oversell is a correctness bug, not a performance bug. The design may sacrifice latency or availability to preserve correctness, but not the reverse. Concretely: if the inventory service cannot confirm a reservation, the order is rejected, not queued optimistically.

- **Hard tenancy isolation by construction.** Each tenant has its own Postgres database. A request reaching the wrong database is a correctness bug; a tenant DB containing another tenant's row is impossible by construction (the database is the boundary). The control-plane catalog is the only shared data store and contains tenant metadata only — no end-customer data, no business data. See Tech Design §1.

- **Eventual consistency, bounded.** Cross-service state converges within 30 seconds at p99 under normal load; under flash-sale burst the bound relaxes to 2 minutes but the system never converges to an oversold state. "Bounded" means we have a measurable SLI and alert on the bound.

- **Idempotency everywhere.** Every message consumer, every webhook receiver, every external-API call is idempotent. Replaying the same event produces the same state. Idempotency keys are scoped per-tenant DB (UNIQUE constraints inside the tenant's own outbox / webhook tables).

- **Observability as a feature.** Every business event is traceable end-to-end (webhook → order → saga → ship → tracking pushback) via a single correlation ID. Every span, log, and metric carries `tenant.id` as a resource attribute, set in routing middleware. Cross-tenant aggregation is a metrics-layer concern (Prometheus / Tempo), never an SQL one.

- **Compliance posture is verifiable, not asserted.** PDPA obligations from §4 map to concrete tests, runbooks, or observability checks. "We're PDPA-compliant" is not a claim the README makes; the README points at the §4 obligation table and the artifacts that satisfy each row.

- **No lock-in to the cloud.** Docker Compose for dev. Production path is plain containers on any Linux host or managed container service; no AWS-only primitives in the critical path. The PgBouncer + Postgres shape works on any standard infrastructure.

---

## 7. Roles and Collaboration

### 7.1 Business Analyst

Discovery: domain research, persona definition, user stories with acceptance criteria, information architecture, business rules (allocation, SLA, zone mapping), **compliance acceptance criteria** (e.g., "given a tenant in archived state, the system DROPs the database within retention window + 24h"). Execution: backlog prep, clarification, demo review. Launch: UAT scenarios, demo script, end-user documentation.

### 7.2 Developer

Discovery: technical feasibility, architecture, ADRs, CI/CD, this document's companion. Execution: implementation, unit + integration + contract tests, **multi-tenant integration tests**, staging deploys, demo. Launch: load and chaos testing, **noisy-neighbor scale gates**, security review, **PDPA architectural review**, production deployment path, API reference.

### 7.3 Collaboration cadence

Weekly sprint rhythm: Monday planning, Wednesday clarification sync, Friday demo + retro. BA writes the story and acceptance criteria; Dev breaks it down and estimates; Dev raises ambiguity early rather than making silent product decisions; BA reviews the demo against acceptance criteria before marking the story done. Sprint demos are the only gate that moves a story into "accepted." **Compliance acceptance criteria are reviewed at sprint demo alongside business acceptance criteria — same gate, no separate compliance phase.**

---

## 8. Feature Scope

### 8.1 Priority matrix

| Priority | Epic | Business value | Scale-risk | Phase |
|---|---|---|---|---|
| P0 | **Tenant Provisioning** (catalog, lifecycle, `shopflow-migrate`) | Critical | Medium — 25-50 DBs, parallel migration runner | 0 |
| P0 | Inventory Hub (reservation, adjustment, availability) | Critical | High — hot-key problem at flash sale | 1 |
| P0 | Inbound (PO, receiving, put-away) | Critical | Low | 1 |
| P0 | Outbound (order, pick wave, pack, ship) | Critical | Medium — saga complexity | 2 |
| P0 | Channel Adapter framework + Shopee mock | Critical | High — per-channel rate limits | 2 |
| P0 | Stock Sync Engine (coalescing, rate limits, priority) | Critical | Highest | 2 |
| P1 | Real-time tracking (SignalR, per-tenant groups) | High | Medium — fan-out cost | 3 |
| P1 | Warehouse layout (zones, bins, capacity) | High | Low | 1 |
| P1 | Allocation engine with rebalance | High | Medium | 2 |
| P1 | Lazada adapter (proves plugin architecture) | High | Low | 2 |
| P2 | Analytics (stock movement, fulfillment funnel) | Medium | Medium — read-model scale, per-tenant | 3 |
| P2 | Barcode/QR scanning flow | Medium | Low | 3 |
| P2 | **Tenant self-service onboarding UI** | Medium | Low — control plane already exists | 3 |
| P3 | Multi-warehouse | Medium | High | Out of scope |
| P3 | Demand forecasting | Low | Very high | Out of scope |
| P3 | Cross-region tenant residency | Medium | Very high | Out of scope |

The Tenant Provisioning epic is the new P0 introduced by the redesign. It is the foundation every other epic depends on; Phase-0 ships it, Phase-1 onwards consumes it.

### 8.2 Epics and user stories

#### Epic 0 — Tenancy (NEW)

- **Story 0.1 — Tenant catalog**. As an operator, I can register a new tenant (name, slug, region, tier, business registration number) via a control-plane CLI or admin API. The system creates a catalog row in `pending` state. Slug is immutable after creation.
- **Story 0.2 — Tenant provisioning**. As the system, when a catalog row enters `provisioning` state, I create the tenant database, apply all current migrations, seed defaults, and transition to `ready`. Failed provisioning leaves the row in `provisioning_failed` state for operator inspection.
- **Story 0.3 — Tenant routing**. As the system, when an HTTP request arrives, I extract the tenant identity from (priority order) explicit header, JWT claim, or subdomain; look up the tenant in the catalog (cached); and scope the request's DbContext to that tenant's database. Cross-tenant routing leakage is a P0 bug.
- **Story 0.4 — Tenant archival**. As an operator, I can mark a tenant for archival via `shopflow-migrate archive --tenant=<slug>`. The catalog transitions to `archiving`; after a configurable retention window (default 30 days), the system DROPs the tenant database and transitions to `archived`. Archive is reversible during the retention window via `shopflow-migrate restore`.
- **Story 0.5 — Migration parallel-by-tenant**. As an operator, I can apply a new migration version to all tenant databases via `shopflow-migrate apply --target=<version> --concurrency=4`. Failures stop the run and report the failed tenant; partial state is the next run's checkpoint.

#### Epic 1 — Inventory

- **Story 1.1 — Centralized stock view**. As a seller, I see all SKUs with total / reserved / allocated / available quantities in one screen. Filterable by category, zone, and stock level; sortable; live-updating via SignalR; warning icon when available ≤ safety_threshold. (All data scoped to the seller's tenant DB.)
- **Story 1.2 — Stock adjustment**. As warehouse staff, I can adjust stock (+/−) with a required reason code (stock take, damaged, lost, returned, other). Audit history retained in the tenant's outbox table. Adjustment triggers async sync to all connected channels. Quantities cannot go negative.
- **Story 1.3 — Stock reservation**. As the system, when an order arrives I reserve stock for 15 minutes via the conditional-INSERT CTE pattern (Tech Design §4). Unconfirmed reservations auto-release. Concurrent reservations on the same SKU must never oversell — this is tested explicitly at 500 concurrent requests in CI and at **5 tenants × 1,000 concurrent in the load suite (noisy-neighbor scale gate)**.

#### Epic 2 — Channel integration

- **Story 2.1 — Connect channel**. OAuth flow (mocked) or API key entry. Auto-pull product list. Map marketplace products to internal SKUs (exact match first, then fuzzy suggestion, then manual). Connection states: connected, syncing, degraded, error, disconnected. Disconnection is reversible without data loss.
- **Story 2.2 — Auto-sync stock to channels**. Stock change → sync to every connected channel at p99 < 30s. Allocation per configured rules (default equal-weight). Per-channel circuit breaker isolates failure blast radius. Failed syncs queue for retry with capped exponential backoff. Every sync attempt is logged with correlation ID + tenant ID.
- **Story 2.3 — Ingest orders**. Webhook receiver validates signature, returns 200 within 200ms, persists raw payload + idempotency key in the **target tenant's** webhook table, and enqueues processing async. Tenant identity is derived from the webhook's authenticated channel context (channel ID is registered against a tenant in the catalog). Duplicate webhooks are silently deduplicated by `(channel_id, provider_event_id)` UNIQUE constraint inside the tenant DB — not by ephemeral Redis state.

#### Epic 3 — Warehouse operations

- **Story 3.1 — Inbound receiving**. Create PO with expected SKU/qty. On receipt, staff enters actual qty; discrepancy auto-creates a reconciliation ticket. System suggests put-away location by zone rules and bin capacity; staff can override.
- **Story 3.2 — Put-away**. Location suggestion algorithm: zone by category, nearest bin with capacity, proximity to packing area (weighted). Bin occupancy updates atomically with stock placement. Bin map is read-optimized for the dashboard.
- **Story 3.3 — Pick wave generation**. Orders in the same 15-minute window (configurable per-tenant) and same shipping profile batch into one pick wave. Within a wave, items group by zone to minimize travel. Waves assign to available pickers. Live progress tracked per wave.
- **Story 3.4 — Pack and ship**. Staff scans/selects picked items, confirms pack. Weight sanity check flags anomalies. Shipping label generated via (mocked) carrier API. Order status → shipped. Stock deduction confirmed, tracking number pushed back to origin marketplace.

#### Epic 4 — Tracking and analytics

- **Story 4.1 — Real-time ops dashboard**. Orders by status, average fulfillment time, picker throughput, channel order volume. Live via SignalR with **per-tenant groups** (a SignalR connection only joins the group for the authenticated tenant). Orders breaching the 2h SLA surface in red with time-since-breach.
- **Story 4.2 — Inventory analytics**. Stock movement by SKU and date range. Low-stock watchlist. Per-channel allocation efficiency (sold/allocated). Category-level turnover. CSV export. **Cross-tenant analytics are explicitly out of scope** — operator reporting across tenants happens at the metrics layer, not via SQL.

---

## 9. Roadmap

### 9.1 Phase summary

```
Phase 0          Phase 1          Phase 2          Phase 3          Phase 4
Foundation +     Core WMS         Multi-Channel    Real-time +      Harden + Ship
Tenancy                           + Sync Engine    Analytics
                                  + Noisy-neighbor

Week 1–2         Week 3–5         Week 6–8         Week 9–10        Week 11–12
```

Each phase has a scale-validation gate: a load test or chaos scenario that the phase must pass before moving on. This is non-negotiable. **Phase-0's gate now includes a multi-tenant routing correctness test in addition to the original startup-time gate.**

### 9.2 Phase 0 — Foundation + Tenancy (Weeks 1–2)

The goal is that `task up` produces a working multi-tenant skeleton end-to-end: control-plane catalog DB created, two dev tenants provisioned, gateway routes a request to the correct tenant DB, auth works per-tenant, a trivial service responds, logs land in Seq with `tenant.id` resource attribute, traces land in Tempo, metrics scrape to Prometheus. Every subsequent phase builds on this skeleton without touching the tenancy layer again.

**BA deliverables**: PRD (this doc, v3.0), wireframes for the four core screens (Dashboard, Inventory, Orders, Channels) **plus tenant onboarding flow**, business-rules document (allocation default, SLA, zone mapping), data dictionary, acceptance-criteria library, **PDPA architectural review checklist**.

**Dev deliverables**: solution layout per Clean Architecture per service, shared kernel (BaseEntity, Result, value objects, domain event base, **`IRequestContext` carrying tenant routing**), control-plane project (`ShopFlow.ControlPlane.{Domain,Infrastructure,Migrations}`), `shopflow-migrate` CLI, contracts project (integration events with `tenant_id` in headers), API Gateway (YARP) with rate limiting, auth middleware, **and tenant routing middleware**, Aspire AppHost with Postgres, **PgBouncer**, Redis, RabbitMQ, Seq, Tempo (OTel collector), Prometheus, MinIO, two mock channel servers, **and two dev tenants provisioned on startup**. JWT auth with refresh + tenant claim. Health endpoints per service. CI (GitHub Actions) running build + unit tests + Testcontainers integration tests **including a tenant-routing correctness test and a migration-attribute smoke test** on PR.

**Scale gate for Phase 0**:
- `task up` cold-starts in < 90s
- Auth happy path is < 150ms p99 locally
- A request with `X-ShopFlow-Tenant: dev1` reads only from the dev1 database; same request with `dev2` reads only from dev2; cross-tenant header attempts are rejected with 403
- Provisioning a new tenant from `pending` to `ready` completes in < 60s p99
- CI pipeline is < 10 min end-to-end

### 9.3 Phase 1 — Core WMS (Weeks 3–5)

Three sprints. Inventory, then inbound, then outbound. At the end of this phase the system can operate a single warehouse per tenant without any marketplace integration.

**Sprint 1 — Inventory.** Stock item aggregate with domain behavior (Reserve, Release, Adjust, ConfirmDeduction). Reservation is implemented against an append-only reservation ledger inside the tenant DB, not by locking the stock row — this is the decision with the largest scale implication and is detailed in Tech Design §4. Conditional-CTE INSERT runs at **READ COMMITTED** isolation (not SERIALIZABLE — see Tech Design §4 for why). `available = total − sum(active reservations) − allocated`, computed via a covering index. Warehouse layout (zones, bins). Domain events (StockChanged, StockReserved, StockReleased, StockAdjusted) emitted via the **per-tenant outbox**. Optimistic concurrency on the stock row for admin-side edits.

**Scale gate (multi-tenant noisy neighbor)**: 5 tenants × 1,000 concurrent reservation requests each, against 1,000 units of stock per tenant. Each tenant sees exactly 1,000 successful reservations, 0 successful reservations from a different tenant, and the remainder explicit failures with a retryable error code. Zero oversell. p99 latency of a reservation call under this load < 200ms per tenant. **Per-tenant fairness floor ≥ 0.85** — the worst-performing tenant's success rate is at least 85% of the best-performing tenant's.

**Sprint 2 — Inbound.** PO, receiving confirmation, discrepancy report, put-away suggestion. MassTransit consumer updates inventory on `InboundConfirmed`, with tenant context carried in the message header. Testcontainers integration test covers the full Postgres + RabbitMQ flow across multiple tenants in parallel.

**Scale gate**: 500 receiving events per tenant in 10 seconds across 3 tenants (1,500 total), every one resulting in a stock update in < 5s p99 (per tenant), no lost events if RabbitMQ is bounced mid-flow (outbox recovers, per tenant).

**Sprint 3 — Outbound.** Order aggregate. Fulfillment saga (MassTransit state machine) with compensation on pick failure. Pick-wave generation via a bounded `Channel<T>` pipeline. Packing + shipping (mocked). Stock deduction confirm event back to Inventory.

**Scale gate**: 2,000 orders per tenant ingested in 1 minute across 3 tenants (6,000 total), all reach packed state within 5 min p99 per tenant. Inject a pick failure for 5% of orders; all failed sagas correctly release their reservations within 60s p99 per tenant.

### 9.4 Phase 2 — Multi-Channel + Sync Engine + Noisy Neighbor (Weeks 6–8)

This is the phase where the design earns its keep on both correctness AND tenant isolation.

**Sprint 4 — Channel adapter framework.** `IChannelAdapter` interface, factory, Shopee mock server with realistic behavior (HMAC-signed webhooks, rate limits advertised via headers, 500/429 injection endpoint for chaos tests). Webhook receiver with persistent idempotency keyed on `(channel_id, provider_event_id)` UNIQUE constraint **inside the target tenant's DB**. Channel-to-tenant mapping in the control-plane catalog so the receiver can route an inbound webhook to the correct tenant. Product mapping engine (exact + fuzzy + manual). Shopee adapter implementation.

**Scale gate**: the same webhook replayed 100 times for the same tenant produces exactly one order. Webhook receiver sustains 1,000 req/s across 5 tenants (200/s each) with p99 < 200ms. A webhook with a tenant-mismatched signature is rejected at the receiver, never reaches a DB.

**Sprint 5 — Stock sync engine.** This is the centerpiece:

- **Coalescing** — per `(tenant, sku, channel)` tuple, only the latest stock value in a debounce window (default 500ms) is pushed; older pending values are dropped. Prevents fan-out amplification when an SKU updates rapidly.
- **Per-channel rate limiting** — token bucket per channel × per tenant, sized to the marketplace's published rate limit. **Tenant A's quota cannot be consumed by tenant B's traffic.**
- **Priority queue** — flash-sale SKUs (manually tagged, or auto-detected by recent velocity) preempt regular SKUs **within a tenant**.
- **Circuit breaker (Polly)** — isolates channel-level failure per tenant.
- **Allocation engine** — rule-based with weights, priorities, max-caps, safety buffer, and rebalance when a channel exhausts its quota. Per tenant.

**Scale gate (the headline noisy-neighbor test)**: 5 tenants concurrently. Tenant A simulates flash sale (2,000 stock changes/second sustained for 5 minutes). Tenants B-E run normal load (50 stock changes/second each). End-to-end sync latency for tenants B-E p99 < 30s **even while tenant A is bursting**. Tenant A's p99 < 90s under burst (degraded but bounded). With one mock channel injecting 30% 500 responses, unaffected channels for unaffected tenants maintain their SLOs. **Per-tenant fairness floor ≥ 0.85.**

**Sprint 6 — Lazada adapter + oversell compensation.** Lazada adapter proves the plugin architecture — the only code change outside `Channel.Infrastructure.Adapters.Lazada` is a line of DI registration. Oversell detection (if sync lag produced an accepted order > available) + compensation flow (alert, reserve from safety buffer or initiate seller-notified cancellation). All scoped per-tenant.

**Scale gate**: end-to-end scenario — flash sale of 10,000 units across two mock channels for tenant A, burst to 30,000 orders/sec for 60s, zero oversell, p99 sync latency < 90s under burst. Tenants B-E unaffected.

### 9.5 Phase 3 — Real-time + Analytics (Weeks 9–10)

**Sprint 7 — Real-time tracking.** SignalR hub with **per-tenant groups** (a connection only joins the group for the authenticated tenant). Order status push. Picker activity feed. Dashboard subscribes via React Query + a SignalR hook with auto-reconnect and gap filling (on reconnect, client requests missed events by last-seen event ID, scoped to the tenant).

**Scale gate**: 500 concurrent dashboard connections per tenant × 5 tenants (2,500 total), fan-out of 1,000 events/sec per tenant, end-to-end push latency p99 < 1s per tenant.

**Sprint 8 — Analytics.** CQRS read model fed by CDC (Phase-3 MVP uses the per-tenant outbox as the CDC source; design doc names the migration path to Debezium at scale-tier 3). Materialized views per tenant for top queries. `IAsyncEnumerable` streaming for large CSV exports. KPI calc (turnover rate, fulfillment time distribution, channel efficiency) per tenant.

**Scale gate**: CSV export of 100K orders for a single tenant streams with constant memory footprint (< 100MB) and p99 first-byte latency < 2s.

### 9.6 Phase 4 — Harden and Ship (Weeks 11–12)

**Sprint 9 — Testing and performance.** Unit coverage > 80% on business logic. Integration coverage of every service interaction across multiple tenants. k6 load tests for the three big flash-sale scenarios. Chaos tests: Redis down, Postgres failover, one channel permanently dead, RabbitMQ partition, **PgBouncer down (must degrade gracefully, not crash)**, **one tenant DB unavailable (other tenants must continue)**. Contract tests (Pact) between services for event shapes. Property-based tests on the allocation engine. **PDPA architectural review** (OWASP top 10 + tenancy isolation + secret handling + sub-processor list current).

**Scale gate**: full load-test suite passes on CI. Chaos scenarios recover without data loss or oversell. Tenant-isolation chaos (one DB down) does not affect others.

**Sprint 10 — Ship.** Docker Compose production config + Aspire AppHost dev config. README with architecture diagram, setup, feature list, scale discussion, screenshots, **PDPA stance one-pager**. Swagger per service. Demo video (< 10 minutes) covering the four core flows **plus the tenant provisioning flow**. Clean git history.

**Scale gate**: a stranger clones the repo, runs `task up`, provisions a new tenant via the CLI, and reaches a working dashboard within 5 minutes. README answers the question "what would you change to serve 100+ tenants?" and "what would the SOC2 follow-up look like?" each in one paragraph.

---

## 10. What We Are Not Building, and Why

Scope discipline is more valuable than scope ambition on a 12-week solo build. Each of these is deferred with a rationale.

- **Real marketplace APIs.** Implementing Shopee and Lazada's real OAuth flows, rate-limit negotiation, and shop onboarding is a month of work by itself, and none of it demonstrates engineering. Mock servers that reproduce the hard parts (signatures, rate limits, 429/5xx behavior, idempotency tokens) demonstrate more engineering per week and let us chaos-test failure modes that real sandboxes cannot reliably inject.
- **Real carrier integrations.** Same logic. Mocked.
- **Multi-warehouse per tenant.** Adding a second warehouse multiplies the inventory allocation problem by a constant but doesn't add architectural novelty over multi-channel allocation, which we already address. Deferred.
- **ML forecasting.** Valuable but orthogonal to the core engineering story. Deferred.
- **Cross-region tenant residency.** PDPA SEA does not require it (yet); same-region deployment satisfies all Phase-1 obligations. Cross-region failover is Phase-3+ infrastructure work — out of scope for portfolio.
- **SOC2 / ISO 27001 control framework.** The architecture supports them, but the controls work (change management, access reviews, vendor risk, incident response runbooks, observation period) is operational, not engineering — out of scope for portfolio.
- **Tier-based hybrid tenancy.** Mixing RLS-shared tenants and DB-per-tenant tenants in the same deployment was considered and explicitly rejected at redesign time as a dual-mental-model burden.
- **Tenant-level encryption-at-rest keys (BYOK).** Postgres TDE + per-tenant KEK rotation is enterprise-tier work, scaffolded but not implemented. Deferred.
- **Tenant data export / portability** (PDPA right to portability for B2B tenants). Possible follow-up; not a Phase-1 obligation.
- **Mobile app.** Web is mobile-responsive. Native app is a later decision.
- **Accounting, invoicing, VN e-invoicing.** Legally required for a real VN SaaS but not part of the engineering demonstration. Deferred.
- **Live tenant migration between clusters.** Operational tooling for "move tenant X from cluster-1 to cluster-2 without downtime" is Phase-3+ work.

---

## 11. Risk Register

| Risk | Impact | Likelihood | Mitigation |
|---|---|---|---|
| **PDPA non-compliance surfaces during audit** | **Very High** | **Medium** | Compliance scope explicit in §4. Architecture choices map to obligations. Quarterly compliance review at sprint retro. |
| **Tenant routing correctness bug leaks data cross-tenant** | **Very High** | **Low** | Routing middleware is the single point of correctness; integration test specifically asserts cross-tenant rejection on every PR. Catalog cache invalidation tested. |
| **PgBouncer becomes a single point of failure under load** | High | Medium | Phase-2 scale gate exercises PgBouncer-down scenario. HA PgBouncer setup documented as Phase-3 infrastructure follow-up. |
| **Tenant DB count exhausts pg_catalog at ~1000+ DBs** | High | Low (within Phase-1/2 scope) | Sharding plan in Tech Design §8 (deferred Phase-3+). MVP ceiling at 50 tenants leaves 20× headroom. |
| Scope creep | High | High | Strict phase gates. Every new idea lands in the "next phase" backlog. Sprint demos enforce the gate. |
| Solo-dev burnout | High | Medium | Phases end on Friday. No weekend work. MVP mindset — skip nice-to-have aggressively. |
| The sync engine is harder than expected | High | Medium | Phase 2 starts with a spike week building the coalescing + rate-limit primitive in isolation before wiring it into channel adapters. |
| **Provisioning automation breaks under a corner case in production** | Medium | Medium | Provisioning is exercised on every dev startup and every integration test (per-test tenant DB). Bugs surface fast. |
| Flash-sale load test infrastructure is expensive to run | Medium | Medium | Load tests run locally on Docker Compose with scaled-down numbers (10% of mid-market peak); we extrapolate rather than run at full scale. |
| Learning curve on MassTransit saga | Medium | Medium | Fallback plan: implement the state machine in-process in the Outbound service domain model if MassTransit saga proves too opaque. Saga pattern is the architectural commitment; MassTransit is the library choice. |
| Portfolio reviewers skim — heavy docs lose them | Medium | High | Both docs are written to be skimmable top-down. Opening sections state the thesis. Diagrams before prose. The PDPA stance is a one-pager in the README. |
| Distributed systems debugging eats sprint time | Medium | High | Observability is built in Phase 0, not retrofitted. Correlation ID, structured logs, traces from day one — every span tagged with `tenant.id`. |

---

## 12. Definition of Done (per story)

A story is done when:

- Code is implemented and self-reviewed (or AI-assisted peer-reviewed).
- Unit tests for business logic are written and passing.
- Integration tests for cross-service flows are written and passing (Testcontainers-backed, **per-test tenant DB** where relevant).
- If the story introduces or modifies an event contract, the contract test is updated. Tenant context in the message header is asserted.
- API endpoints are documented in Swagger with example payloads. Tenant routing behavior documented per endpoint.
- BA has reviewed the demo against acceptance criteria, **including compliance acceptance criteria where applicable**.
- The phase's scale gate is still passing.
- Merged to main, deployed to staging via Aspire / Docker Compose, no regression.

---

## 13. Tools

| Tool | Purpose |
|---|---|
| GitHub Projects | Kanban, sprint tracking |
| GitHub Issues | Stories, bugs, ADRs via issue template |
| GitHub Wiki | Documentation, retros |
| Excalidraw | Architecture diagrams, whiteboarding |
| Figma | Wireframes |
| Seq | Structured logs (every entry tagged with `tenant.id`) |
| Grafana + Prometheus + Tempo | Metrics + traces dashboards (cross-tenant aggregation lives here) |
| k6 | Load testing, including noisy-neighbor scenarios |
| NBomber | .NET-native load testing for in-process scenarios |
| Toxiproxy | Chaos injection for integration tests, including PgBouncer fault injection |
| Pact | Contract tests |
| `shopflow-migrate` | Per-tenant migration runner (custom, Phase-0 deliverable) |
| `shopflow-gate` | Phase-gate verification CLI (carry-over from v2.0) |

Maintained by BA + Dev collaboration. Reviewed weekly at sprint retro. The technical companion (`02-technical-design-document.md`) is the source of truth for architecture, scale reasoning, SLO design, multi-tenancy mechanics, and the tier-by-tier roadmap of what changes at 5 / 50 / 500 tenants.
