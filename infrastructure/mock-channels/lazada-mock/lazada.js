// Lazada-specific endpoint handlers and signing canonicalization.
//
// Canonical signing string per Lazada Open Platform:
//   Sort all request params (including the api path as a leading element per Lazada docs)
//   alphabetically by key, concatenate as `key1value1key2value2...`, then HMAC-SHA256
//   with the app secret. The result is hex-encoded and uppercased.
//
// Webhook signature header per Lazada:
//   `X-Lazop-Signature: <hex hmac>`
//   `X-Lazop-Timestamp: <millis>`
//   `X-Lazop-App-Key: <key>`
//
// Wire shapes are taken verbatim from `tests/fixtures/channels/lazada/`. Where the fixtures
// have `EXAMPLE_*` placeholders, the mock substitutes deterministic synthetic values.

import { computeHmacSha256 } from '@shopflow/mock-channels-shared';

const APP_KEY = 'EXAMPLE_APP_KEY';
const SELLER_ID = 'EXAMPLE_SELLER_ID_NUMERIC';

export function mountLazadaRoutes({ app, secret, logger }) {
    // GET /products/get — wire shape from fixtures/lazada/api-product-list-response.json.
    app.get('/products/get', (req, res) => {
        const timestampMs = Date.now();
        const sign = lazadaSignParams(secret, {
            app_key: APP_KEY,
            timestamp: String(timestampMs),
            sign_method: 'sha256',
            access_token: req.query.access_token ?? 'EXAMPLE_ACCESS_TOKEN',
            api: '/products/get',
            filter: req.query.filter ?? 'all',
            offset: String(req.query.offset ?? 0),
            limit: String(req.query.limit ?? 50),
        });
        res.status(200).json({
            code: '0',
            type: '',
            message: '',
            request_id: 'mock-lazada-request-id-0001',
            sign,
            data: {
                total_products: 3,
                products: [
                    {
                        item_id: 9990001000,
                        primary_category: 12345,
                        attributes: {
                            name: 'EXAMPLE Wireless Mouse — Black',
                            brand: 'EXAMPLE_BRAND',
                            model: 'EXAMPLE_MODEL_M1',
                        },
                        skus: [
                            {
                                Status: 'active',
                                quantity: 25,
                                ShopSku: 'EXAMPLE-LZD-SHOP-SKU-001',
                                SellerSku: 'EXAMPLE-SKU-MOUSE-BLK-A',
                                price: '250000',
                                special_price: '225000',
                                Available: 25,
                                _currency: 'VND',
                            },
                        ],
                    },
                    {
                        item_id: 9990001001,
                        primary_category: 12345,
                        attributes: { name: 'EXAMPLE USB-C Hub — 7-in-1', brand: 'EXAMPLE_BRAND' },
                        skus: [
                            {
                                Status: 'active',
                                quantity: 12,
                                SellerSku: 'EXAMPLE-SKU-HUB-001',
                                price: '850000',
                                Available: 12,
                                _currency: 'VND',
                            },
                        ],
                    },
                    {
                        item_id: 9990001002,
                        primary_category: 12345,
                        attributes: { name: 'EXAMPLE Mousepad XL' },
                        skus: [
                            {
                                Status: 'inactive',
                                quantity: 0,
                                SellerSku: 'EXAMPLE-SKU-PAD-XL',
                                price: '150000',
                                Available: 0,
                                _currency: 'VND',
                            },
                        ],
                    },
                ],
            },
        });
    });

    // GET /orders/get — minimal happy-path order list.
    app.get('/orders/get', (_req, res) => {
        res.status(200).json({
            code: '0',
            type: '',
            message: '',
            request_id: 'mock-lazada-request-id-0002',
            data: {
                count: 1,
                orders: [
                    {
                        order_id: 'EXAMPLE_LAZADA_TRADE_ORDER_ID',
                        statuses: ['ready_to_ship'],
                        seller_id: SELLER_ID,
                        site: 'vn',
                    },
                ],
            },
        });
    });

    // POST /order/pack — happy-path pack acknowledgement.
    app.post('/order/pack', (req, res) => {
        const orderItemIds = req.body?.order_item_ids ?? [];
        logger.info({ orderItemIds }, 'lazada order/pack acknowledged');
        res.status(200).json({
            code: '0',
            type: '',
            message: '',
            request_id: 'mock-lazada-request-id-0003',
            data: { pack_id: 'EXAMPLE_PACK_ID', order_item_ids: orderItemIds },
        });
    });
}

/**
 * Build the Lazada canonical signing string and HMAC it. Lazada sorts params
 * lexicographically then concatenates `key1value1key2value2...`, prepending the
 * api path. Returns the HMAC as uppercase hex (Lazada convention).
 */
export function lazadaSignParams(secret, params) {
    const keys = Object.keys(params).sort();
    const concatenated = keys.map((k) => `${k}${params[k]}`).join('');
    return computeHmacSha256(secret, concatenated, 'hex').toUpperCase();
}

/**
 * Returns a webhook signer suitable for `WebhookDispatcher`. Honors `mode`:
 *   - 'valid'             : real HMAC over the body
 *   - 'clock-skew-3min'   : real HMAC, but X-Lazop-Timestamp is +3 min (engine pre-shifted)
 *   - 'wrong-secret'      : HMAC computed with a deliberately wrong secret
 *   - 'missing'           : X-Lazop-Signature header omitted
 */
export function lazadaWebhookSigner({ secret }) {
    return (payloadBytes, { event, timestampMs, mode }) => {
        const headers = {
            'X-Lazop-Timestamp': String(timestampMs),
            'X-Lazop-App-Key': APP_KEY,
            'X-Lazop-Event-Type': event,
            'X-Request-Id': `mock-lazada-${timestampMs}`,
        };
        if (mode === 'missing') {
            return { headers };
        }
        const signingSecret = mode === 'wrong-secret' ? `${secret}-WRONG` : secret;
        const sig = lazadaSignParams(signingSecret, {
            app_key: APP_KEY,
            timestamp: String(timestampMs),
            event,
            body_sha256: computeHmacSha256(signingSecret, payloadBytes.toString('utf8'), 'hex'),
        });
        headers['X-Lazop-Signature'] = sig;
        return { headers };
    };
}
