# Shopee Mock-Channel Server

Impersonates Shopee Open Platform v2 for ShopFlow WMS dev / test. Listens on **port 7001** by default.

## Why "shopee-mock" exists separately from "lazada-mock"

Per `infrastructure/mock-channels/README.md`, the per-marketplace folders carry only what
genuinely differs between marketplaces:

1. Signing canonicalization — Shopee canonical string is `partner_id|api_path|timestamp|access_token|shop_id`.
2. Endpoint paths — `/api/v2/product/...`, `/api/v2/order/...`.
3. Webhook header names — `Authorization` (the hex HMAC) + `X-Shopee-Push-Event-Type`.

Everything else (control plane, scenario engine, AJV schema, request logger, webhook dispatcher) is shared.

## Endpoints exposed

| Method | Path                                     | Source of shape |
|--------|------------------------------------------|-----------------|
| GET    | `/healthz`                               | `200 {"status":"ok","service":"shopee-mock","version":"<v>"}` |
| GET    | `/api/v2/product/get_item_list`          | `tests/fixtures/channels/shopee/api-product-list-response.json` |
| POST   | `/api/v2/order/get_order_list`           | Synthesised from the order-status webhook fixture |
| POST   | `/api/v2/order/ship_order`               | Minimal happy-path ack |
| POST   | `/control/scenario/{name}/start`         | Shared control plane (see repo README) |
| POST   | `/control/scenario/stop`                 | Shared control plane |
| GET    | `/control/state`                         | Shared control plane |
| GET    | `/control/scenarios`                     | Shared control plane (lists loaded scenario names) |
| POST   | `/control/webhook/register`              | Shared control plane |
| POST   | `/control/webhook/deliver`               | Shared control plane |

## Scenarios shipped

| Name                                  | YAML file                                  |
|---------------------------------------|--------------------------------------------|
| `429-with-weird-retry-after`          | [scenarios/429-with-weird-retry-after.yml](scenarios/429-with-weird-retry-after.yml) |
| `webhook-redelivered-after-200-ack`   | [scenarios/webhook-redelivered-after-200-ack.yml](scenarios/webhook-redelivered-after-200-ack.yml) |
| `signature-clock-skew-3min`           | [scenarios/signature-clock-skew-3min.yml](scenarios/signature-clock-skew-3min.yml) |
| `partial-body-then-eof`               | [scenarios/partial-body-then-eof.yml](scenarios/partial-body-then-eof.yml) |
| `5xx-burst-30s`                       | [scenarios/5xx-burst-30s.yml](scenarios/5xx-burst-30s.yml) |

## Environment

| Variable                  | Default                       | Purpose |
|---------------------------|-------------------------------|---------|
| `PORT`                    | `7001`                        | HTTP listen port |
| `SHOPEE_PARTNER_SECRET`   | `shopee-mock-dev-secret`      | HMAC secret used by the webhook signer and the API endpoint signature |
| `MOCK_VERSION`            | `dev`                         | Reported by `/healthz` |
| `LOG_LEVEL`               | `info`                        | Pino log level |

## Manual smoke test (after U9 wires `task up`)

```bash
curl -s http://localhost:7001/healthz
curl -s http://localhost:7001/api/v2/product/get_item_list
curl -s -X POST http://localhost:7001/control/scenario/429-with-weird-retry-after/start
curl -s http://localhost:7001/api/v2/product/get_item_list -i   # expect HTTP/1.1 429
curl -s -X POST http://localhost:7001/control/scenario/stop
```
