# AGENTS.md — Inbound module deltas

Per root AGENTS.md §11.82 this file captures Inbound-specific invariants only. Rules that apply repo-wide live in the root canon; do not restate them here.

## Hard "do not simplify"

- **Per-line receiving is the canonical granularity.** A PO can have many receiving sessions over time (partial delivery); each session confirms one or more lines. Do not collapse to whole-PO receiving — the SEA marketplace flow ships partial deliveries and the operator has to record what physically arrived line-by-line. (Sprint-2-redux R4.)
- **Idempotency anchor is `UNIQUE(receiving_id, purchase_order_line_id)`** on `receiving_lines`. Duplicate confirmation attempts for the same `(receiving_id, line_id)` resolve at the index level — do not switch to application-layer dedupe. (Sprint-2-redux R6.)
- **Discrepancy is auto-accept + ticket, not block.** When `actual_qty != expected_qty` the receive succeeds, the actual quantity goes into inventory, and a `reconciliation_tickets` row is written with `Open` status. Do not flip to blocking — Product Plan §9.3 specifies the ticket pattern and warehouse operators must be able to record what physically arrived without admin intervention. (Sprint-2-redux R9.)
- **`PurchaseOrderLine.RecordReceipt` is internal-to-aggregate.** Only `PurchaseOrder.RecordLineReceipt` invokes it so the parent recomputes its own state atomically. Do not expose the line method publicly.

## Cross-module contract

- **Cross-module event is `ShopFlow.Contracts.Inbound.InboundConfirmedV1`.** Emitted per confirmed line; payload carries `(po_id, line_id, receiving_id, sku, actual_qty, bin_id, tenant_id, occurred_at)`. Inventory consumes via `InboundConfirmedConsumer`. Idempotency on the consumer side is the `inbound_dedup` table in the Inventory tenant DB keyed on `(receiving_id, line_id)`. (Sprint-2-redux R10, R11, R15.)

## Module conventions

- Composition entry point: `services.AddShopFlowDefaults(...)` then `services.AddInboundModule(IConfiguration)`. The `AddShopFlowDefaults` call lands in U7 along with the Inventory equivalent.
- `InboundDbContext` is constructed via the scoped registration `services.AddScoped<InboundDbContext>(sp => ...)` reading `IRequestContext.DbConnectionString` — same pattern as Inventory.
- Migrations live in `ShopFlow.Inbound.Infrastructure/Migrations/`. Hand-authored with `[Migration]` + `[DbContext]` attributes per AGENTS.md §3.23. `OnConfiguring` overrides `PendingModelChangesWarning` per [docs/solutions/2026-05-13-ef9-pendingmodelchangeswarning-with-hand-authored-migrations.md](../../../docs/solutions/2026-05-13-ef9-pendingmodelchangeswarning-with-hand-authored-migrations.md).
