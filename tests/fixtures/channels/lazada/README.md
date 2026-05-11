# Lazada Channel Fixtures

Plain-text fixtures derived from Lazada Open Platform documentation, used as canonical wire-shape inputs for:

- `infrastructure/mock-channels/lazada-mock` — the mock-channel server (U7 of the Phase-0 plan) replays these payloads to exercise idempotency, signature verification, and failure-injection scenarios.
- `tests/ShopFlow.*.IntegrationTests/` — integration tests that need a realistic-shape webhook or API body without depending on a live mock running.

## Provenance

| Field | Value |
|---|---|
| Source | Lazada Open Platform public documentation |
| Captured | 2026-04-27 |
| Method | Hand-authored from documented schemas + sample payloads in the public dev portal |
| Live capture | **No** — these are not snapshots of real live traffic |
| Synthetic | **Yes — every identifier, name, price, and ID is an `EXAMPLE_*` placeholder or a synthetic numeric value** |

Lazada APIs are documented at the regional Lazada Open Platform portal. Schemas for VN/SG/MY/TH/ID/PH share most shapes with site-specific deltas (e.g., currency, address structure).

## Field-by-field real-vs-synthetic disposition

| Field category | Disposition | Notes |
|---|---|---|
| Response envelope (`code`, `type`, `message`, `request_id`, `data`) | **Real shape** | `code` is a string ("0" for success, error codes documented in error reference). |
| Push-message envelope (`message_id`, `message_type`, `site`, `seller_id`, `timestamp`, `data`) | **Real shape, synthetic values** | `message_id` is the idempotency key; `(channel_id, message_id)` UNIQUE in our schema. |
| HMAC signature header (`X-Lazop-Signature`, `X-Lazop-Timestamp`) | **Synthetic placeholder** | Real signatures use HMAC-SHA256 over the canonical request string per Lazada's signing rules. The mock-channel server (U7) computes real HMACs at request time. |
| Order IDs (`trade_order_id`, `trade_order_line_id`) | **Synthetic** (`EXAMPLE_*`) | Lazada uses numeric strings or alphanumeric IDs depending on endpoint. |
| Buyer info | **Synthetic / hashed** | Lazada anonymizes buyer info in webhooks by default (returns hash). Address details accessible only via dedicated endpoint. |
| SKU / product fields | **Synthetic, real shape** | `SellerSku` is integrator-controlled; `ShopSku` and `item_id` are Lazada-controlled. |
| Currency, prices | **Real shape** (string-encoded decimals, VND major units) | Lazada returns prices as strings to avoid float rounding (note: NOT minor-units integer like Shopee VND). |
| Pagination (`offset`, `total_products`) | **Real shape** | Lazada uses offset-based pagination with `total_products` (or `total_records`) for total count. |
| Status enums (`active`, `inactive`, `pending`, `ready_to_ship`) | **Real values** | Documented enums. |

## How to use these in tests

1. Load the fixture as JSON.
2. Pass the body through the mock-channel server's HMAC signer to generate a valid signature header (mock servers in U7 expose this as a control-plane endpoint).
3. POST to the webhook receiver under test.
4. Assert: 200 OK; webhook event row exists with `(channel_id, message_id) UNIQUE`; second POST with the same body is silently 200 without re-processing.

## Lazada-specific gotchas worth designing around

- **Price format mismatch with Shopee**: Lazada returns prices as strings of major-unit decimals (`"225000"` for VND 225,000); Shopee returns prices as integer minor units (which for VND is the same number since VND has no minor unit, but for currencies with cents the difference matters). Cross-channel SKU value normalization should happen at the Channel module's adapter layer.
- **Message ID is the only safe idempotency key**. Do NOT use `request_id` (that's per-request, not per-event).
- **Push messages may arrive out of order**. Design state-machine transitions to be tolerant of late arrivals (saga state's `previous_status` field helps here).

## When to refresh

- Lazada announces a major API version bump or signing-method change (e.g., move from `sign_method=sha256` to a new scheme) — schedule a refresh ADR.
- A real integration spike captures payloads that materially differ in shape — replace `_meta.synthetic = true` with a `_meta.captured_at` real date and `_meta.synthetic = false`, document the seller_id anonymization in this README.
