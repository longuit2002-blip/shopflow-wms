# Mock-Channel Servers — ShopFlow WMS Phase-0

Two near-identical Node.js / Express servers that impersonate the Shopee Open Platform v2 and Lazada Open Platform marketplace APIs. They are the dev/test substitutes that the Inventory + Channel modules dial against until live sandbox credentials land in Phase 2+.

> **Why this exists**: per `01-product-development-plan.md.docx` §348 ("the mocking IS the engineering") and `02-technical-design-document.md.docx` §9 (Webhook Ingest), correctness of webhook idempotency, signature timing, and rate-limit-respecting retry behaviour can only be verified against a controllable stand-in. This repo treats the mock servers as a first-class piece of the system, not as fixtures.

## Shape at a glance

```
infrastructure/mock-channels/
  README.md                     <-- you are here
  _shared/                      shared library (HMAC, control plane, scenario engine, request log)
  shopee-mock/                  Shopee-specific endpoints + scenarios (port 7001)
  lazada-mock/                  Lazada-specific endpoints + scenarios (port 7002)
```

Per-server code differs ONLY in:

1. **Signature canonicalization**. Shopee canonical string is `partner_id|api_path|timestamp|access_token|shop_id`; Lazada canonical string is the sort-and-concatenate-params rule per Lazada's signing docs.
2. **Endpoint paths and payload shapes**. Shopee `/api/v2/order/...`; Lazada `/orders/...`.
3. **Webhook header names**. Shopee uses `Authorization` + `X-Shopee-Push-Event-Type`; Lazada uses `X-Lazop-Signature` + `X-Lazop-Timestamp`.

Everything else — scenario YAML schema, control-plane API, fault-injection state machine, request logging, healthz endpoint — is shared verbatim from `_shared/`.

## Control-plane API (identical on both servers)

| Method | Path                                  | Body / Query                                          | Behaviour |
|--------|---------------------------------------|-------------------------------------------------------|-----------|
| POST   | `/control/scenario/{name}/start`      | (none)                                                | Activates the named scenario. Returns `200 {"active":"<name>","startedAt":"<ISO>"}`. 404 if unknown. |
| POST   | `/control/scenario/stop`              | (none)                                                | Clears any active scenario. Returns `200 {"active":null}`. |
| GET    | `/control/state`                      | (none)                                                | Returns `200 {"active":"<name>"\|null,"sinceMs":<elapsed>}`. |
| POST   | `/control/webhook/register`           | `{"target":"http://...","events":["order.created"]}` | Registers a webhook delivery target (in-memory). |
| POST   | `/control/webhook/deliver`            | `{"event":"order.created","payload":{...}}`          | Delivers the payload to every registered target with a real HMAC, honouring the active scenario's `webhookDeliveryRules`. |

The control plane is intentionally side-band; it is mounted at `/control/*` so it cannot collide with marketplace paths.

## Scenario YAML format

Each scenario file in `shopee-mock/scenarios/` and `lazada-mock/scenarios/` follows this contract, validated at server start by `_shared/scenarioSchema.js` (AJV). Server startup refuses to load malformed files.

```yaml
name: "429-with-weird-retry-after"
description: "Marketplace returns 429 with non-integer Retry-After. Tests parser robustness."
behavior:
  responses:
    - matchPath: "/api/v2/.*"   # regex
      matchMethod: "*"          # or "GET" | "POST" | "PUT" | "DELETE"
      returnStatus: 429
      returnHeaders:
        Retry-After: "garbage-not-a-number"
        Content-Type: "application/json"
      returnBody: '{"error":"rate_limit","code":429}'
      repeat: "until-stopped"   # or an integer count
  webhookDeliveryRules: []
```

For the webhook redelivery scenario:

```yaml
name: "webhook-redelivered-after-200-ack"
description: "Every triggered delivery sends the payload twice with a 5-second gap. Tests idempotency-via-(channel_id, provider_event_id)."
behavior:
  responses: []
  webhookDeliveryRules:
    - eventPattern: "*"         # or a literal event name
      deliveryCount: 2
      gapMs: 5000
      signatureMode: "valid"    # or "clock-skew-3min" | "wrong-secret"
```

## Five named scenarios (per server)

Both servers ship the same five scenario names; the per-marketplace YAML adapts the response body and headers to that wire format.

| Name                                  | Purpose |
|---------------------------------------|---------|
| `429-with-weird-retry-after`          | Tests parser robustness in the consumer's rate-limit-respecting retry loop. |
| `webhook-redelivered-after-200-ack`   | Tests idempotency persistence (`(channel_id, provider_event_id) UNIQUE`). |
| `signature-clock-skew-3min`           | Tests the receiver's allowed-skew window per Tech Design §9.4. |
| `partial-body-then-eof`               | Tests the consumer's partial-response handling. |
| `5xx-burst-30s`                       | Tests circuit-breaker / exponential-backoff behaviour. |

## Tooling pins

- Node `22-alpine` Docker base image
- `express` 4.21.0
- `ajv` 8.17.1
- `js-yaml` 4.1.0
- `pino` 9.5.0
- `pino-http` 10.3.0

`_shared/` is consumed by the two server packages via a `file:` reference. It is `"private": true` and is never published.

## Boundaries

- **No persistence.** Webhook targets and active scenarios are in-memory only. Restart wipes state.
- **Single-tenant by construction.** The mock does not impersonate multi-tenant marketplaces; tenancy is the consumer's concern.
- **Constant-time HMAC comparison.** `_shared/hmac.js` uses `crypto.timingSafeEqual` per Tech Design §9.4.
- **Fixtures, not capture.** Response shapes are taken from `tests/fixtures/channels/{shopee,lazada}/` (the U3 deliverable). EXAMPLE_* placeholders are replaced with deterministic synthetic values.

## How this lands in the dev orchestrator

U9 wires both servers into the dev stack (Aspire AppHost or docker-compose, per ADR-0001). Until then, the Dockerfiles in each server folder are the build contract.
