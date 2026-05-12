# AGENTS.md — Inbound module deltas

Per root AGENTS.md §11.82 this file captures Inbound-specific invariants only. Rules that apply repo-wide live in the root canon; do not restate them here.

## Hard "do not simplify"

- **Webhook idempotency persistence happens BEFORE enqueue** per root AGENTS.md §6.39. Raw payload + `(channel_id, provider_event_id) UNIQUE` lands in Postgres atomically; the message bus enqueue is a side effect that may retry. Do not invert the order; do not switch to a Redis dedupe layer.
- **Handlers carry `[Idempotent]`** per AGENTS.md §6.40 — analyzer ShopFlow0003 enforces. Receipts without it fail the build.

## U9 stub state

Schema-only placeholders (`InboundModuleMarker`, empty `InboundDbContext`, 501 controller). The real shape lands in Phase-1 Sprint-2 along with the channel-side webhook framework.
