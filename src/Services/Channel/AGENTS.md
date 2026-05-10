# ShopFlow.Channel — module deltas

This module owns: marketplace adapters (Shopee, Lazada, TikTok Shop, Shopify), webhook ingest with persistent idempotency, and the stock-sync engine (coalescing buffer + per-channel token bucket + priority queue) per Tech Design §8 / §9. Aggregates: `ChannelBinding`, `WebhookEvent`, `OutboundStockSyncJob` (Phase-2). Integration events emitted: `ChannelOrderReceivedEvent`, `StockSyncDispatchedEvent`. Integration events consumed: `StockReservedEvent`, `OrderShippedEvent` (drives stock-sync triggers).

Deltas from root [`AGENTS.md`](../../../AGENTS.md):

1. Webhook receivers persist the raw payload + `(channel_id, provider_event_id) UNIQUE` BEFORE enqueuing — never Redis-only dedupe (root rule 36 elevated to a hard, do-not-simplify in this module).
2. The stock-sync engine's coalescing buffer is load-bearing: collapse successive `StockChanged` deltas for the same `(channel, sku)` into one outbound call. Do NOT replace with naive per-event push, even when "it looks faster" — Tech Design §8.3 explains the per-channel rate-limit bin packing this protects.

## Lifecycle invariants
- A webhook event row, once persisted, is never deleted; replays mark `processed_at` and increment `delivery_count`. Audit immutability depends on this.
- Per-channel HMAC adapters are pluggable (matches the mock-channel `_shared/` pattern from `infrastructure/mock-channels/`); the dispatcher itself stays marketplace-agnostic.

## Phase-0 status
This is a SKELETON: csprojs compile, `AddChannelModule` is empty, the API hosts only `/healthz`. Phase-2 Sprint-4/5 (W6-7) implements the adapter framework + sync engine per Tech Design §8 / §9.
