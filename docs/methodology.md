# ShopFlow WMS — AI-Assisted Development Methodology

A chronological case study of building ShopFlow WMS — a 12-week portfolio Warehouse Management System for SEA marketplaces — using Claude Code + the compound-engineering skill cadence. Seven sprints, one solo developer, .NET 9 + Postgres + modular monolith. This doc captures what worked, what didn't, and the patterns that compounded across sprints.

---

## Table of Contents

- [Context — what this doc is, and what it's not](#context--what-this-doc-is-and-what-its-not)
- [How the project was built — chronological sprint narrative](#how-the-project-was-built--chronological-sprint-narrative)
  - [Phase-0-redux — Foundation (DB-per-tenant pivot)](#phase-0-redux--foundation-db-per-tenant-pivot)
  - [Sprint-1-redux — Reservation ledger](#sprint-1-redux--reservation-ledger)
  - [Sprint-2-redux — Inbound module](#sprint-2-redux--inbound-module)
  - [Sprint-2.5 — Cross-module outbox prefix](#sprint-25--cross-module-outbox-prefix)
  - [Sprint-3-redux — Outbound saga](#sprint-3-redux--outbound-saga)
  - [Sprint-4 — Channel webhook ingress](#sprint-4--channel-webhook-ingress)
  - [Sprint-4.5 — Webhook follow-up + scale gate](#sprint-45--webhook-follow-up--scale-gate)
  - [Sprint-5 — Stock sync engine (egress)](#sprint-5--stock-sync-engine-egress)
- [Synthesis — patterns that compounded across sprints](#synthesis--patterns-that-compounded-across-sprints)
  - [Cadence: brainstorm → plan → work → sign-off](#cadence-brainstorm--plan--work--sign-off)
  - [KTD discovery: plan-time vs mid-sprint emergence](#ktd-discovery-plan-time-vs-mid-sprint-emergence)
  - [Subagent dispatch: context isolation under pressure](#subagent-dispatch-context-isolation-under-pressure)
  - [Deferral pattern: Sprint-4 → 4.5, Sprint-5 → 5.5](#deferral-pattern-sprint-4--45-sprint-5--55)
  - [Context management: AGENTS.md / CLAUDE.md / session-resume hooks](#context-management-agentsmd--claudemd--session-resume-hooks)
- [Friction — what didn't work, what cost more than expected](#friction--what-didnt-work-what-cost-more-than-expected)
- [Forward-looking — open questions, what would be different next time](#forward-looking--open-questions-what-would-be-different-next-time)
- [Appendix — reference inventory](#appendix--reference-inventory)

---

## Context — what this doc is, and what it's not

This is a single-project case study. ShopFlow WMS is a 12-week portfolio Warehouse Management System for SEA marketplaces (Shopee, Lazada, TikTok Shop), built solo on a .NET 9 + Postgres + modular-monolith stack. Across seven sprints (Phase-0-redux through Sprint-5), the project shipped: a database-per-tenant routing foundation, an append-only reservation ledger with atomic CTE-based oversell protection, modules for inbound (PO receiving), outbound (fulfillment saga), channel ingress (marketplace webhook receivers), and channel egress (a four-layer isolation pipeline pushing stock updates back to marketplaces). The codebase is at [github.com/longuit2002-blip/shopflow-wms](https://github.com/longuit2002-blip/shopflow-wms).

The methodology was Claude Code + the compound-engineering plugin's skill cadence: `/ce-brainstorm` for product decisions, `/ce-plan` for technical decisions, `/ce-work` for execution. Persistent context lives in `AGENTS.md` and [CLAUDE.md](../CLAUDE.md); per-sprint artifacts live in `docs/brainstorms/`, `docs/plans/`, and `docs/phase-gates/`. Institutional learnings — things future-self should re-discover only once — live in [docs/solutions/](solutions/).

What this doc is not: a universal methodology claim. The patterns described worked for **one project, one solo developer, one stack, one tool combination**. They may not generalize. Specifically: solo work removes coordination overhead that a team would face; long-running project (7+ sprints) lets persistent docs amortize their cost; Claude Code's specific skill primitives shape the cadence; .NET 9 + Postgres has its own friction modes that other stacks don't. Read this as evidence, not prescription.

If you came expecting "AI saved me X% time" or "AI 10x'd my output" — this is not that doc. There are no productivity multipliers measured here. What's measured is: 7 sprints shipped to tag, 50+ commits with conventional messages, multiple emergent design decisions caught either at plan-time or mid-sprint, several friction modes that cost real time and that future projects would benefit from anticipating. The honest claim is: this methodology let one solo developer ship more rigorous architecture than they would have shipped without it, at a cost paid mostly in documentation overhead.

The reader this doc is written for: future-self, six months from now, starting a new project and trying to remember what compounded vs what wasted effort. Secondary reader: a developer who clones this repo and wants to understand how it was built without reading every sign-off doc.

---

## How the project was built — chronological sprint narrative

*Each section follows the same shape: what was built (one or two sentences), Key Technical Decisions (planned + emergent), deferrals (Skip'd slots and scope cuts), what worked, what surfaced friction, and reference links.*

### Phase-0-redux — Foundation (DB-per-tenant pivot)

*(Section body lands in U2.)*

### Sprint-1-redux — Reservation ledger

*(Section body lands in U2.)*

### Sprint-2-redux — Inbound module

*(Section body lands in U3.)*

### Sprint-2.5 — Cross-module outbox prefix

*(Section body lands in U3.)*

### Sprint-3-redux — Outbound saga

*(Section body lands in U3.)*

### Sprint-4 — Channel webhook ingress

*(Section body lands in U4.)*

### Sprint-4.5 — Webhook follow-up + scale gate

*(Section body lands in U4.)*

### Sprint-5 — Stock sync engine (egress)

*(Section body lands in U5.)*

---

## Synthesis — patterns that compounded across sprints

*(Section body lands in U6 — pattern catalog with 5 subsections plus 1-2 Mermaid diagrams.)*

### Cadence: brainstorm → plan → work → sign-off

### KTD discovery: plan-time vs mid-sprint emergence

### Subagent dispatch: context isolation under pressure

### Deferral pattern: Sprint-4 → 4.5, Sprint-5 → 5.5

### Context management: AGENTS.md / CLAUDE.md / session-resume hooks

---

## Friction — what didn't work, what cost more than expected

*(Section body lands in U7 — 6+ named friction modes with Pattern / Cost / Mitigation framing.)*

---

## Forward-looking — open questions, what would be different next time

*(Section body lands in U7 — open questions, process improvements that would not be in scope but are worth surfacing for project sau.)*

---

## Appendix — reference inventory

All artifacts the chronological narrative and synthesis sections reference. Grouped by category.

### Brainstorms — `docs/brainstorms/`

- [2026-05-12-sprint-2-redux-inbound-requirements.md](brainstorms/2026-05-12-sprint-2-redux-inbound-requirements.md) — Sprint-2-redux Inbound module scope (PurchaseOrder + Receiving + cross-module outbox).
- [2026-05-13-sprint-3-redux-outbound-requirements.md](brainstorms/2026-05-13-sprint-3-redux-outbound-requirements.md) — Sprint-3-redux Outbound module scope (saga + picking + shipping).
- [2026-05-14-sprint-4.5-webhook-followup-requirements.md](brainstorms/2026-05-14-sprint-4.5-webhook-followup-requirements.md) — Sprint-4.5 closure scope (4 Sprint-4 deferrals).
- [2026-05-16-sprint-5-stock-sync-requirements.md](brainstorms/2026-05-16-sprint-5-stock-sync-requirements.md) — Sprint-5 Stock Sync Engine scope (4-layer isolation pipeline).
- [2026-05-16-sprint-5-visual.html](brainstorms/2026-05-16-sprint-5-visual.html) — Sprint-5 visual companion when prose dialogue got confusing.
- [2026-05-18-methodology-writeup-requirements.md](brainstorms/2026-05-18-methodology-writeup-requirements.md) — this writeup's own origin doc.

### Plans — `docs/plans/`

- [2026-05-11-001-redesign-multi-tenancy-db-per-tenant-plan.md](plans/2026-05-11-001-redesign-multi-tenancy-db-per-tenant-plan.md) — the plan-of-plans for the DB-per-tenant pivot.
- [2026-05-11-002-phase-0-redux-bootstrap-plan.md](plans/2026-05-11-002-phase-0-redux-bootstrap-plan.md) — Phase-0-redux foundation plan.
- [2026-05-11-003-phase-1-sprint-1-redux-reservation-ledger-plan.md](plans/2026-05-11-003-phase-1-sprint-1-redux-reservation-ledger-plan.md) — Sprint-1-redux reservation ledger plan.
- [2026-05-13-001-feat-phase-1-sprint-2-redux-inbound-plan.md](plans/2026-05-13-001-feat-phase-1-sprint-2-redux-inbound-plan.md) — Sprint-2-redux Inbound plan.
- [2026-05-13-002-feat-phase-1-sprint-3-redux-outbound-plan.md](plans/2026-05-13-002-feat-phase-1-sprint-3-redux-outbound-plan.md) — Sprint-3-redux Outbound plan.
- [2026-05-13-003-feat-phase-2-sprint-4-channel-webhook-plan.md](plans/2026-05-13-003-feat-phase-2-sprint-4-channel-webhook-plan.md) — Sprint-4 Channel webhook plan.
- [2026-05-14-001-feat-phase-2-sprint-4.5-webhook-followup-plan.md](plans/2026-05-14-001-feat-phase-2-sprint-4.5-webhook-followup-plan.md) — Sprint-4.5 webhook follow-up plan.
- [2026-05-16-001-feat-phase-2-sprint-5-stock-sync-plan.md](plans/2026-05-16-001-feat-phase-2-sprint-5-stock-sync-plan.md) — Sprint-5 Stock Sync Engine plan.
- [2026-05-18-001-feat-methodology-writeup-plan.md](plans/2026-05-18-001-feat-methodology-writeup-plan.md) — this writeup's own plan.

### Sign-offs — `docs/phase-gates/`

- [2026-05-12-phase-0-redux-signoff.md](phase-gates/2026-05-12-phase-0-redux-signoff.md) — Phase-0-redux completion.
- [2026-05-12-sprint-1-redux-signoff.md](phase-gates/2026-05-12-sprint-1-redux-signoff.md) — Sprint-1-redux completion.
- [2026-05-13-sprint-2-redux-signoff.md](phase-gates/2026-05-13-sprint-2-redux-signoff.md) — Sprint-2-redux completion.
- [2026-05-13-sprint-2.5-signoff.md](phase-gates/2026-05-13-sprint-2.5-signoff.md) — Sprint-2.5 closure (cross-module outbox prefix).
- [2026-05-13-sprint-3-redux-signoff.md](phase-gates/2026-05-13-sprint-3-redux-signoff.md) — Sprint-3-redux completion.
- [2026-05-13-sprint-4-signoff.md](phase-gates/2026-05-13-sprint-4-signoff.md) — Sprint-4 completion.
- [2026-05-15-sprint-4.5-signoff.md](phase-gates/2026-05-15-sprint-4.5-signoff.md) — Sprint-4.5 closure (4 Sprint-4 deferrals).
- [2026-05-17-sprint-5-signoff.md](phase-gates/2026-05-17-sprint-5-signoff.md) — Sprint-5 completion.

### Architectural Decision Records — `docs/adr/`

- [0001-aspire-vs-docker-compose.md](adr/0001-aspire-vs-docker-compose.md) — Aspire for dev orchestration, Docker Compose for prod.
- [0002-modular-monolith-first.md](adr/0002-modular-monolith-first.md) — modular monolith first, microservice split deferred.
- [0003-database-per-tenant-for-compliance.md](adr/0003-database-per-tenant-for-compliance.md) — PDPA SEA hard isolation drives DB-per-tenant.

### Institutional learnings — `docs/solutions/`

- [2026-05-10-ef-migration-needs-attributes.md](solutions/2026-05-10-ef-migration-needs-attributes.md) — hand-authored EF migrations silently no-op without `[Migration]` + `[DbContext]` attributes.
- [2026-05-12-readcommitted-conditional-cte-correctness.md](solutions/2026-05-12-readcommitted-conditional-cte-correctness.md) — READ COMMITTED + conditional-CTE INSERT correctness (v3.0 correction to v2.0 SERIALIZABLE).
- [2026-05-13-cross-module-outbox-table-name-collision.md](solutions/2026-05-13-cross-module-outbox-table-name-collision.md) — Sprint-2-redux U9 finding that drove Sprint-2.5 closure.
- [2026-05-13-ef9-pendingmodelchangeswarning-with-hand-authored-migrations.md](solutions/2026-05-13-ef9-pendingmodelchangeswarning-with-hand-authored-migrations.md) — EF Core 9 `PendingModelChangesWarning` mitigation for hand-authored migrations.
- [2026-05-13-multi-row-cte-predicate-must-live-in-update.md](solutions/2026-05-13-multi-row-cte-predicate-must-live-in-update.md) — Sprint-3-redux K11 — multi-row CTE concurrency, predicate must live in UPDATE, not in pre-check.

### Git tags

| Tag | Date | Scope |
|---|---|---|
| `v0.1.0-phase-0` | (archived) | Original Phase-0 work, superseded by Phase-0-redux pivot. |
| `v0.2.0-phase-0-redux` | 2026-05-12 | Phase-0-redux foundation (DB-per-tenant + ControlPlane + Aspire + migrate CLI). |
| `v0.3.0-sprint-1-redux` | 2026-05-12 | Sprint-1-redux reservation ledger. |
| `v0.4.0-sprint-2-redux` | 2026-05-13 | Sprint-2-redux Inbound module + RabbitMQ flip. |
| `v0.4.1-sprint-2.5` | 2026-05-13 | Sprint-2.5 cross-module outbox prefix. |
| `v0.5.0-sprint-3-redux` | 2026-05-13 | Sprint-3-redux Outbound saga + scale gate. |
| `v0.6.0-sprint-4` | 2026-05-13 | Sprint-4 Channel webhook ingress. |
| `v0.6.1-sprint-4.5` | 2026-05-15 | Sprint-4.5 webhook follow-up + 3 scale-gate bodies. |
| `v0.7.0-sprint-5` | 2026-05-17 | Sprint-5 Stock Sync Engine (4-layer isolation pipeline). |
| `v0.8.0-methodology-writeup` | 2026-05-18 | This methodology writeup (no source code changes). |

---

*Snapshot dated 2026-05-18. Future-self updates this doc when new patterns surface or old patterns turn out wrong.*
