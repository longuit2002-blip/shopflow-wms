# AGENTS.md — Analytics module deltas

Per root AGENTS.md §11.82 this file captures Analytics-specific invariants only.

## Hard "do not simplify"

- **Read-side only** per root AGENTS.md §11.76 — no Domain project. The csproj quartet is intentionally a triplet (Application + Infrastructure + Api).
- **Projections are recomputable from the event stream.** Do not introduce write-time aggregation that loses the source events; the reservation ledger + outbox messages are the source of truth.
- **No cross-tenant aggregation in the same query.** Per ADR-0003 every dashboard query is tenant-scoped at the DB-connection level; the routing middleware binds the tenant DB before the handler runs.

## U9 stub state

Schema-only placeholders. Real read-side projections land in Phase-2.
