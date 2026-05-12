# AGENTS.md — Outbound module deltas

Per root AGENTS.md §11.82 this file captures Outbound-specific invariants only.

## Hard "do not simplify"

- **Saga compensation is mandatory** per Tech Design v3.0 §9. Every Reserve → Pick → Pack → Ship step has a documented `OnFailure` that releases the reservation and rolls back picked stock. Do not introduce an Outbound step without its compensation.
- **MassTransit saga state persists in Postgres** at MVP per AGENTS.md §6.44; Redis is the scale option, not the default. Do not swap the persistence in Phase-1.

## U9 stub state

Schema-only placeholders. The real saga state machine + projection tables land in Phase-1 Sprint-3.
