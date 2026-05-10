# ShopFlow.Outbound — module deltas

This module owns: order fulfillment orchestration (Reserve, Pick, Pack, Ship) via the MassTransit-backed `FulfillmentSaga`. Aggregates: `Order`, `Pick`, `Pack`, `Shipment` (Phase-1 Sprint-3). Integration events emitted: `OrderReleasedForFulfillmentEvent`, `OrderShippedEvent`. Integration events consumed: `StockReservedEvent`, `StockReservationFailedEvent` (from Inventory).

Deltas from root [`AGENTS.md`](../../../AGENTS.md):

1. No deltas — module follows the root canon verbatim, especially §11 module shape.

## Lifecycle invariants
- The fulfillment saga is THE coordination primitive — no ad-hoc handler chains. Every state transition (`Reserved → Picked → Packed → Shipped`) is explicit; compensation paths (`ReservationFailed`, `PickShortage`) trigger explicit transitions to `Cancelled` per Tech Design §10.
- The saga never publishes `IPublishEndpoint.Publish` directly during a write; outgoing integration events go through the outbox like every other write (root rule 35). The saga's MassTransit-driven publish path is configured to route through `OutboxInterceptor` — do not bypass.

## Phase-0 status
This is a SKELETON: csprojs compile, `AddOutboundModule` is empty, the API hosts only `/healthz`. Phase-1 Sprint-3 (W5) implements the saga + Reserve/Pick/Pack/Ship transitions per Tech Design §10.
