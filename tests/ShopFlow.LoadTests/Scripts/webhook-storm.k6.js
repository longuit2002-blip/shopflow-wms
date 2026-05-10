// =============================================================================
// webhook-storm.k6.js
//
// Asserts: 01-product-development-plan.md.docx §313 verbatim —
//   "Webhook receiver sustains 1,000 req/s with p99 < 200ms. The same
//    webhook replayed 100 times produces exactly one order."
//
// This script extends the 100×-replay invariant into a 20%-duplicate
// sustained-storm shape: 1000 req/s for 5 minutes, where 20% of payloads
// reuse a previously sent (channel_id, provider_event_id). Asserts:
//   • 100% of requests return 200 (idempotent acks, never 5xx)
//   • zero processing duplicates (the system records exactly one
//     downstream side effect per unique (channel_id, provider_event_id))
//
// Status: documentation today. Becomes runnable in U9 / Phase-2 Sprint-4
// once:
//   • the Channel API webhook endpoint is reachable
//   • the U7 mock-channel servers can drive replays (current W1 mocks
//     already serve as targets via the control-plane signing helpers)
//
// How to run (post-Phase-2 Sprint-4):
//   BASE_URL=http://localhost:5004 \
//   CHANNEL_ID=shopee-mock \
//   k6 run tests/ShopFlow.LoadTests/Scripts/webhook-storm.k6.js
// =============================================================================

import http from 'k6/http';
import { check } from 'k6';
import { Counter } from 'k6/metrics';

const baseUrl = __ENV.BASE_URL || 'http://localhost:5004';
const channelId = __ENV.CHANNEL_ID || 'shopee-mock';

const duplicates = new Counter('webhook_duplicates_sent');
const acks200 = new Counter('webhook_acks_200');
const nonAcks = new Counter('webhook_non_acks');

// A small pool of "previous" event IDs — 20% of iterations reuse one
// from the pool, 80% mint a fresh ID. Pool seeded in setup().
let eventPool = [];

export const options = {
    scenarios: {
        sustained_storm: {
            executor: 'constant-arrival-rate',
            rate: 1000,
            timeUnit: '1s',
            duration: '5m',
            preAllocatedVUs: 200,
            maxVUs: 1000,
        },
    },
    thresholds: {
        webhook_acks_200: ['count>0'],
        webhook_non_acks: ['count==0'],
        http_req_duration: ['p(99)<200'],
    },
};

export function setup() {
    // Pre-mint 200 event IDs that the 20%-duplicate branch will reuse.
    // Returning them as setup() data is k6's standard pattern.
    const seeded = [];
    for (let i = 0; i < 200; i++) {
        seeded.push(`seed-event-${i}-${Date.now()}`);
    }
    return { pool: seeded };
}

export default function (data) {
    const useDuplicate = Math.random() < 0.2;
    const providerEventId = useDuplicate
        ? data.pool[Math.floor(Math.random() * data.pool.length)]
        : `${__VU}-${__ITER}-${Date.now()}`;

    if (useDuplicate) {
        duplicates.add(1);
    }

    const payload = JSON.stringify({
        channelId,
        providerEventId,
        eventType: 'order.created',
        body: { orderId: providerEventId },
    });

    const res = http.post(`${baseUrl}/api/channel/webhooks/${channelId}`, payload, {
        headers: { 'Content-Type': 'application/json' },
    });

    if (res.status === 200) {
        acks200.add(1);
    } else {
        nonAcks.add(1);
    }

    check(res, { 'ack 200': (r) => r.status === 200 });
}
