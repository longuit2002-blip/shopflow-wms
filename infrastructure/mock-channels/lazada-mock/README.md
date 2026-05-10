# Lazada Mock-Channel Server

Impersonates Lazada Open Platform for ShopFlow WMS dev / test. Listens on **port 7002** by default.

## Why "lazada-mock" exists separately from "shopee-mock"

Per `infrastructure/mock-channels/README.md`, the per-marketplace folders carry only what
genuinely differs between marketplaces:

1. Signing canonicalization — Lazada sorts params alphabetically and concatenates `key1value1key2value2...` with the api path, then HMAC-SHA256, hex-encoded uppercase.
2. Endpoint paths — `/products/...`, `/orders/...`, `/order/...`.
3. Webhook header names — `X-Lazop-Signature` + `X-Lazop-Timestamp` + `X-Lazop-App-Key`.

Everything else (control plane, scenario engine, AJV schema, request logger, webhook dispatcher) is shared.

## Endpoints exposed

| Method | Path                                | Source of shape |
|--------|-------------------------------------|-----------------|
| GET    | `/healthz`                          | `200 {"status":"ok","service":"lazada-mock","version":"<v>"}` |
| GET    | `/products/get`                     | `tests/fixtures/channels/lazada/api-product-list-response.json` |
| GET    | `/orders/get`                       | Synthesised from the order-status webhook fixture |
| POST   | `/order/pack`                       | Minimal happy-path ack |
| POST   | `/control/scenario/{name}/start`    | Shared control plane |
| POST   | `/control/scenario/stop`            | Shared control plane |
| GET    | `/control/state`                    | Shared control plane |
| GET    | `/control/scenarios`                | Shared control plane |
| POST   | `/control/webhook/register`         | Shared control plane |
| POST   | `/control/webhook/deliver`          | Shared control plane |

## Scenarios shipped

| Name                                  | YAML file                                  |
|---------------------------------------|--------------------------------------------|
| `429-with-weird-retry-after`          | [scenarios/429-with-weird-retry-after.yml](scenarios/429-with-weird-retry-after.yml) |
| `webhook-redelivered-after-200-ack`   | [scenarios/webhook-redelivered-after-200-ack.yml](scenarios/webhook-redelivered-after-200-ack.yml) |
| `signature-clock-skew-3min`           | [scenarios/signature-clock-skew-3min.yml](scenarios/signature-clock-skew-3min.yml) |
| `partial-body-then-eof`               | [scenarios/partial-body-then-eof.yml](scenarios/partial-body-then-eof.yml) |
| `5xx-burst-30s`                       | [scenarios/5xx-burst-30s.yml](scenarios/5xx-burst-30s.yml) |

## Environment

| Variable                | Default                       | Purpose |
|-------------------------|-------------------------------|---------|
| `PORT`                  | `7002`                        | HTTP listen port |
| `LAZADA_APP_SECRET`     | `lazada-mock-dev-secret`      | HMAC secret used by the webhook signer and the API endpoint signature |
| `MOCK_VERSION`          | `dev`                         | Reported by `/healthz` |
| `LOG_LEVEL`             | `info`                        | Pino log level |

## Manual smoke test (after U9 wires `task up`)

```bash
curl -s http://localhost:7002/healthz
curl -s http://localhost:7002/products/get
curl -s -X POST http://localhost:7002/control/scenario/5xx-burst-30s/start
curl -s http://localhost:7002/products/get -i   # expect HTTP/1.1 503 for 30s
```
