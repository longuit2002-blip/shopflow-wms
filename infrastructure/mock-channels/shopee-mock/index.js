// Shopee mock-channel server entry point.
//
// Per AGENTS.md §1 (in-tree, machine-agnostic) and Tech Design §9 (Webhook Ingest), this
// process replays the Shopee Open Platform v2 wire format with controllable failure
// injection. All cross-cutting infrastructure lives in `../_shared/`; only Shopee-specific
// canonicalization, endpoint shapes, and webhook header names live here.

import express from 'express';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';

import {
    createControlPlaneRouter,
    createLogger,
    createRequestLogger,
    ScenarioEngine,
    WebhookDispatcher,
} from '@shopflow/mock-channels-shared';

import { mountShopeeRoutes, shopeeWebhookSigner } from './shopee.js';

const PORT = Number.parseInt(process.env.PORT ?? '7001', 10);
const SECRET = process.env.SHOPEE_PARTNER_SECRET ?? 'shopee-mock-dev-secret';
const VERSION = process.env.MOCK_VERSION ?? 'dev';

const logger = createLogger({ marketplace: 'shopee', level: process.env.LOG_LEVEL ?? 'info' });

async function main() {
    const engine = new ScenarioEngine({ logger, marketplace: 'shopee' });
    const here = dirname(fileURLToPath(import.meta.url));
    await engine.loadDirectory(join(here, 'scenarios'));

    const dispatcher = new WebhookDispatcher({
        engine,
        signer: shopeeWebhookSigner({ secret: SECRET }),
        logger,
    });

    const app = express();
    app.disable('x-powered-by');
    app.use(express.json({ limit: '1mb' }));
    app.use(createRequestLogger({ logger }));

    // Scenario interception runs before route handlers so a /api/v2/* path can be
    // turned into a 429 / 503 / partial-body response by the active scenario.
    app.use((req, res, next) => {
        if (req.path.startsWith('/control') || req.path === '/healthz') {
            return next();
        }
        if (engine.maybeApply(req, res)) {
            return undefined;
        }
        return next();
    });

    app.get('/healthz', (_req, res) => {
        res.status(200).json({ status: 'ok', service: 'shopee-mock', version: VERSION });
    });

    app.use('/control', createControlPlaneRouter({ engine, dispatcher, logger }));

    mountShopeeRoutes({ app, secret: SECRET, logger });

    app.use((err, _req, res, _next) => {
        logger.error({ err: err?.message, stack: err?.stack }, 'unhandled error');
        res.status(500).json({ error: 'internal_error' });
    });

    app.listen(PORT, () => {
        logger.info({ port: PORT, scenarios: engine.listNames() }, 'shopee-mock listening');
    });
}

main().catch((err) => {
    logger.error({ err: err?.message, stack: err?.stack }, 'shopee-mock failed to start');
    process.exit(1);
});
