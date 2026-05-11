# Shopee Channel Fixtures

Plain-text fixtures derived from Shopee Open Platform v2 documentation, used as canonical wire-shape inputs for:

- `infrastructure/mock-channels/shopee-mock` — the mock-channel server (U7 of the Phase-0 plan) replays these payloads to exercise idempotency, signature verification, and failure-injection scenarios.
- `tests/ShopFlow.*.IntegrationTests/` — integration tests that need a realistic-shape webhook or API body without depending on a live mock running.

## Provenance

| Field | Value |
|---|---|
| Source | Shopee Open Platform v2 public documentation |
| Captured | 2026-04-27 |
| Method | Hand-authored from documented schemas + sample payloads in the public dev portal |
| Live capture | **No** — these are not snapshots of real live traffic |
| Synthetic | **Yes — every identifier, name, address, and ID is an `EXAMPLE_*` placeholder or a synthetic numeric value** |

If you need real-shape fixtures captured from production, register a Shopee Open Platform sandbox app and capture from there — the documented schemas should be a faithful guide, but vendor APIs evolve.

## Field-by-field real-vs-synthetic disposition

| Field category | Disposition | Notes |
|---|---|---|
| Top-level event envelope (`code`, `shop_id`, `timestamp`, `data`) | **Real shape, synthetic values** | `code` integer values are documented (3 = order push); `timestamp` is Unix seconds UTC. |
| Order numbers (`ordersn`) | **Synthetic** (`EXAMPLE_*`) | Real Shopee order serials follow a documented pattern but format details may vary by region. |
| HMAC signature header | **Synthetic placeholder** | Real signatures use HMAC-SHA256 over `partner_id|api_path|timestamp|access_token|shop_id`; the mock-channel server (U7) computes real HMACs at request time. |
| Currency, total amounts | **Real shape** (VND minor units, integer) | VND uses smallest-unit integers (no fractional). |
| Recipient address | **Synthetic** (`EXAMPLE_*` strings) | Privacy-protected by design; real fixtures from sandbox tend to have anonymized buyers anyway. |
| Item / model / SKU identifiers | **Synthetic** | Pattern follows `EXAMPLE-SKU-<NAME>-<VARIANT>` for clarity. |
| Logistics status enums | **Real values** | `LOGISTICS_NOT_START`, `LOGISTICS_PICKUP_DONE`, etc. are documented enums. |
| Cancel reasons | **Real values** | `BUYER_CANCEL`, `SYSTEM_CANCEL`, etc. are documented enums. |
| Pagination (`has_next_page`, `next_offset`) | **Real shape** | Shopee v2 uses offset-based pagination; `next_offset = 0` signals end. |
| `request_id` | **Synthetic UUIDv4** | Shopee echoes this back from the request. Used by the integrator for idempotency, not by Shopee. |

## How to use these in tests

1. Load the fixture as JSON (`System.Text.Json.JsonDocument.Parse`).
2. Pass the body through the mock-channel server's HMAC signer to generate a valid signature header (mock servers in U7 expose this as a control-plane endpoint).
3. POST to the webhook receiver under test.
4. Assert: 200 OK; webhook event row exists with `(channel_id, provider_event_id) UNIQUE`; second POST with the same body is silently 200 without re-processing.

For failure-injection scenarios (signature clock skew, partial body, redelivery after 200), see `infrastructure/mock-channels/shopee-mock/scenarios/*.yml` (lands in U7).

## When to refresh

- Shopee announces a major API version bump (v3, v4) — schedule a refresh ADR.
- A real integration spike captures payloads that materially differ in shape — replace `_meta.synthetic = true` with a `_meta.captured_at` real date and `_meta.synthetic = false`, document the shop ID anonymization in this README.
