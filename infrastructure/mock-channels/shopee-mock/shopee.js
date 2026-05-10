// Shopee-specific endpoint handlers and signing canonicalization.
//
// Canonical signing string per Shopee Open Platform v2:
//   `{partner_id}{api_path}{timestamp}{access_token}{shop_id}`   (no separators in real Shopee;
//    we use literal pipe '|' in our internal canonical form for readability since the mock
//    is the only consumer of its own canonical form. The HMAC over either form is constant-time
//    comparable as long as both sides agree.)
//
// Webhook signature header per Shopee:
//   `Authorization: <hex hmac>`
//   `X-Shopee-Push-Event-Type: <event>`
//
// Wire shapes are taken verbatim from `tests/fixtures/channels/shopee/`. Where the fixtures
// have `EXAMPLE_*` placeholders, the mock substitutes deterministic synthetic values.

import { computeHmacSha256 } from '@shopflow/mock-channels-shared';

const SHOP_ID = 9999000111;
const PARTNER_ID = 'EXAMPLE_PARTNER_ID';

export function mountShopeeRoutes({ app, secret, logger }) {
    // GET /api/v2/product/get_item_list — wire shape from fixtures/shopee/api-product-list-response.json.
    app.get('/api/v2/product/get_item_list', (req, res) => {
        const timestamp = Math.floor(Date.now() / 1000);
        const sign = computeHmacSha256(
            secret,
            shopeeCanonical({
                partnerId: PARTNER_ID,
                apiPath: '/api/v2/product/get_item_list',
                timestamp,
                accessToken: req.query.access_token ?? 'EXAMPLE_ACCESS_TOKEN',
                shopId: SHOP_ID,
            }),
            'hex',
        );
        res.status(200).json({
            request_id: req.query.request_id ?? 'mock-shopee-request-id-0001',
            error: '',
            message: '',
            sign,
            response: {
                item: [
                    { item_id: 1234567000, item_status: 'NORMAL', update_time: timestamp - 1000, tag: [] },
                    { item_id: 1234567001, item_status: 'NORMAL', update_time: timestamp - 2000, tag: [] },
                    { item_id: 1234567002, item_status: 'BANNED', update_time: timestamp - 5000, tag: ['kit'] },
                ],
                total_count: 3,
                has_next_page: false,
                next_offset: 0,
            },
        });
    });

    // POST /api/v2/order/get_order_list — minimal happy-path order list.
    app.post('/api/v2/order/get_order_list', (_req, res) => {
        const timestamp = Math.floor(Date.now() / 1000);
        res.status(200).json({
            request_id: 'mock-shopee-request-id-0002',
            error: '',
            message: '',
            response: {
                more: false,
                next_cursor: '',
                order_list: [
                    {
                        order_sn: 'EXAMPLE_2604ABCDEFGH',
                        order_status: 'READY_TO_SHIP',
                        update_time: timestamp,
                    },
                ],
            },
        });
    });

    // POST /api/v2/order/ship_order — happy-path shipping ack.
    app.post('/api/v2/order/ship_order', (req, res) => {
        const ordersn = req.body?.ordersn ?? 'EXAMPLE_2604ABCDEFGH';
        logger.info({ ordersn }, 'shopee ship_order acknowledged');
        res.status(200).json({
            request_id: 'mock-shopee-request-id-0003',
            error: '',
            message: '',
            response: { ordersn, status: 'SHIPMENT_BOOKED' },
        });
    });
}

/**
 * Build the Shopee canonical signing string. Exposed so tests / receivers can reuse it.
 */
export function shopeeCanonical({ partnerId, apiPath, timestamp, accessToken, shopId }) {
    return `${partnerId}|${apiPath}|${timestamp}|${accessToken}|${shopId}`;
}

/**
 * Returns a webhook signer suitable for `WebhookDispatcher`. Honors `mode`:
 *   - 'valid'             : real HMAC over the body
 *   - 'clock-skew-3min'   : real HMAC, but the X-Shopee-Push-Timestamp header is +3 min
 *                           (our scenario engine has already shifted timestampMs)
 *   - 'wrong-secret'      : HMAC computed over the body with a deliberately wrong secret
 *   - 'missing'           : Authorization header omitted entirely
 */
export function shopeeWebhookSigner({ secret }) {
    return (payloadBytes, { event, timestampMs, mode }) => {
        const headers = {
            'X-Shopee-Push-Event-Type': event,
            'X-Shopee-Push-Timestamp': String(Math.floor(timestampMs / 1000)),
            'X-Request-Id': `mock-shopee-${timestampMs}`,
        };
        if (mode === 'missing') {
            return { headers };
        }
        const signingSecret = mode === 'wrong-secret' ? `${secret}-WRONG` : secret;
        const canonical = `${PARTNER_ID}|/webhook/shopee|${Math.floor(timestampMs / 1000)}|${payloadBytes.toString('utf8')}`;
        headers.Authorization = computeHmacSha256(signingSecret, canonical, 'hex');
        return { headers };
    };
}
