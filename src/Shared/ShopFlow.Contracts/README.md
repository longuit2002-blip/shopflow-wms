# ShopFlow.Contracts

Wire-compatible integration event records that cross bounded-context boundaries via MassTransit.

## Status (W1, Phase-0)

Placeholder. Concrete event records land in:

- **U6** — Inventory module emits `StockReservedEvent`, `StockReleasedEvent`, `StockAdjustedEvent`, `StockChangedEvent`.
- **U10** — Outbound, Channel, Inbound, Analytics modules emit their integration events here.

Defining a real event in U5 is premature; the project exists so that `ShopFlow.SharedKernel` and any U6+ module can reference it without churn.

## Conventions

Per `AGENTS.md` sections 6 and 7:

- Every published integration event carries `tenant_id`, `correlation_id`, and `occurred_at` (UTC) in its envelope.
- Records are immutable C# `record` types named past-tense participle (`StockReservedEvent`, `OrderShipped`).
- No domain references — value objects translate to primitives at the boundary.

See `docs/source/02-technical-design-document.md.txt` §11 (outbox) and §16 (correlation propagation).
