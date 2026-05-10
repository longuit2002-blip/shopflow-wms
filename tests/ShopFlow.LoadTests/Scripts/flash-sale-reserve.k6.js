// =============================================================================
// flash-sale-reserve.k6.js
//
// Asserts: 01-product-development-plan.md.docx §299 verbatim —
//   "5,000 concurrent reservation requests against 1,000 units of stock
//    produce exactly 1,000 successful reservations, 4,000 explicit failures
//    with a retryable error code, and zero oversell. p99 latency of a
//    reservation call under this load < 200ms."
//
// Status: documentation today. Becomes runnable in U9 / W3 once:
//   • the Inventory API is reachable at $BASE_URL (U9 wires the dev
//     orchestrator)
//   • the reservation ledger is implemented (Phase-1 Sprint-1, W3)
//
// How to run (post-U9):
//   BASE_URL=http://localhost:5001 \
//   TENANT_ID=11111111-1111-1111-1111-111111111111 \
//   SKU=FLASH-SKU-1 TOTAL_UNITS=1000 \
//   k6 run tests/ShopFlow.LoadTests/Scripts/flash-sale-reserve.k6.js
// =============================================================================

import http from 'k6/http';
import { check } from 'k6';
import { Counter } from 'k6/metrics';

const baseUrl = __ENV.BASE_URL || 'http://localhost:5001';
const tenantId = __ENV.TENANT_ID || '11111111-1111-1111-1111-111111111111';
const sku = __ENV.SKU || 'FLASH-SKU-1';
const totalUnits = parseInt(__ENV.TOTAL_UNITS || '1000', 10);

const successes = new Counter('reservation_successes');
const oversoldFailures = new Counter('reservation_oversold');
const otherFailures = new Counter('reservation_other_failures');

export const options = {
    scenarios: {
        flash_sale: {
            executor: 'shared-iterations',
            vus: 500,
            iterations: 5000,
            maxDuration: '60s',
        },
    },
    thresholds: {
        // Plan §299: zero oversell — successes must equal totalUnits, exactly.
        reservation_successes: [`count==${totalUnits}`],
        // 5000 - 1000 = 4000 explicit oversold failures.
        reservation_oversold: [`count==${5000 - totalUnits}`],
        // No 5xx, no client errors that aren't OVERSOLD.
        reservation_other_failures: ['count==0'],
        // Plan §299 latency target.
        http_req_duration: ['p(99)<200'],
    },
};

export default function () {
    const orderId = `k6-${__VU}-${__ITER}`;
    const payload = JSON.stringify({
        tenantId,
        sku,
        qty: 1,
        orderId,
    });
    const res = http.post(`${baseUrl}/api/inventory/reservations`, payload, {
        headers: { 'Content-Type': 'application/json', 'X-Tenant-Id': tenantId },
    });

    if (res.status === 200 || res.status === 201) {
        successes.add(1);
    } else if (res.status === 409 && res.body && res.body.indexOf('OVERSOLD') >= 0) {
        oversoldFailures.add(1);
    } else {
        otherFailures.add(1);
    }

    check(res, {
        'response is 2xx or OVERSOLD-409': (r) =>
            r.status === 200 ||
            r.status === 201 ||
            (r.status === 409 && r.body && r.body.indexOf('OVERSOLD') >= 0),
    });
}
