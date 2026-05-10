# ShopFlow.Analytics — module deltas

This module owns: read-side reporting projections + query API. Aggregates: NONE — per Tech Design §5 Analytics is read-side only (no Domain layer; the trio is `Application + Infrastructure + Api`). Integration events emitted: NONE. Integration events consumed: `StockReservedEvent`, `OrderShippedEvent`, `InboundReceivedEvent` (drives projections).

Deltas from root [`AGENTS.md`](../../../AGENTS.md):

1. **No Domain layer.** Analytics is the documented exception to root rule 6 / §11.73. Every projection, materialization, and query lives in `Application` (DTOs, query handlers) or `Infrastructure` (read-model DbContext + event consumers). Do NOT introduce aggregates here — that intent belongs in the upstream write-side module.
2. The read-model DbContext is **read-only from the API side**: query handlers project into DTOs, never mutate. Mutations come exclusively from MassTransit consumers reacting to upstream integration events (single direction, single source of truth).

## Lifecycle invariants
- Projections are eventually consistent. The query API surfaces a `projected_at` timestamp on every endpoint so callers can reason about staleness — never silently serve stale data as if fresh.
- Integration-event handlers are idempotent (root rule 35-36): replays project the same row idempotently, never duplicate.

## Phase-0 status
This is a SKELETON: csprojs compile, `AddAnalyticsModule` is empty, the API hosts only `/healthz`. Phase-3 Sprint-7 (W9) implements the read-model + projections per Tech Design §5.
