# ShopFlow.Inbound — module deltas

This module owns: purchase-order ingest and goods-receiving workflows. Aggregates: `PurchaseOrder`, `ReceivingRecord` (Phase-1 Sprint-2). Integration events emitted: `InboundReceivedEvent`, `InboundConfirmedEvent`. Integration events consumed: `PurchaseOrderApprovedEvent` (from upstream procurement, when it lands).

Deltas from root [`AGENTS.md`](../../../AGENTS.md):

1. No deltas — module follows the root canon verbatim, especially §11 module shape.

## Lifecycle invariants
- `ReceivingRecord` is append-only: corrections are new records that reference the prior one, never row updates. This protects audit and reconciliation against silent overwrites.
- Quantity-discrepancy resolution at receive time always emits a domain event; the saga in Outbound never fabricates inventory.

## Phase-0 status
This is a SKELETON: csprojs compile, `AddInboundModule` is empty, the API hosts only `/healthz`. Phase-1 Sprint-2 (W4) implements the domain — DbContext, entity configurations for `purchase_orders` / `receiving_records`, and MediatR handlers per Tech Design §6.
