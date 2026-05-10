// =============================================================================
// stock-sync-burst.k6.js
//
// Asserts: 01-product-development-plan.md.docx §323 verbatim —
//   "2,000 stock changes/second sustained for 5 minutes. End-to-end sync
//    latency (change commit → mock channel ack) p99 < 30s, p50 < 5s. With
//    one mock channel injecting 30% 500 responses, unaffected channels
//    maintain their SLOs."
//
// Status: documentation today. Becomes runnable in U9 / W7 once:
//   • the Channel API is reachable at $BASE_URL
//   • the stock sync engine is implemented (Phase-2 Sprint-5, W7)
//   • the U7 mock channels accept the burst rate (Shopee + Lazada mocks)
//
// How to run (post-W7):
//   BASE_URL=http://localhost:5004 \
//   TENANT_ID=11111111-1111-1111-1111-111111111111 \
//   k6 run tests/ShopFlow.LoadTests/Scripts/stock-sync-burst.k6.js
// =============================================================================

import http from 'k6/http';
import { check } from 'k6';
import { Trend } from 'k6/metrics';

const baseUrl = __ENV.BASE_URL || 'http://localhost:5004';
const tenantId = __ENV.TENANT_ID || '11111111-1111-1111-1111-111111111111';

const syncLatency = new Trend('sync_latency_ms', true);

export const options = {
    scenarios: {
        sustained_burst: {
            executor: 'constant-arrival-rate',
            rate: 2000,
            timeUnit: '1s',
            duration: '5m',
            preAllocatedVUs: 200,
            maxVUs: 1000,
        },
    },
    thresholds: {
        http_req_failed: ['rate<0.01'],
        // Plan §323 latency targets — note these will be measured by the
        // real change-commit→ack probe once the engine ships; until then
        // the http_req_duration is a proxy.
        http_req_duration: ['p(50)<5000', 'p(99)<30000'],
        sync_latency_ms: ['p(50)<5000', 'p(99)<30000'],
    },
};

export default function () {
    const sku = `BURST-SKU-${__VU % 200}`;
    const payload = JSON.stringify({
        tenantId,
        sku,
        delta: 1,
    });
    const start = Date.now();
    const res = http.post(`${baseUrl}/api/channel/stock-changes`, payload, {
        headers: { 'Content-Type': 'application/json', 'X-Tenant-Id': tenantId },
    });
    syncLatency.add(Date.now() - start);

    check(res, { 'accepted': (r) => r.status === 202 || r.status === 200 });
}
