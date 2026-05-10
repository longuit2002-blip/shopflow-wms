// Lazada mock-channel server entry point.
//
// Same shape as `../shopee-mock/index.js`. Per the consistency requirement in
// `infrastructure/mock-channels/README.md`, only the imported `mountLazadaRoutes` and
// `lazadaWebhookSigner` differ; everything else is shared verbatim.

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

import { mountLazadaRoutes, lazadaWebhookSigner } from './lazada.js';

const PORT = Number.parseInt(process.env.PORT ?? '7002', 10);
const SECRET = process.env.LAZADA_APP_SECRET ?? 'lazada-mock-dev-secret';
const VERSION = process.env.MOCK_VERSION ?? 'dev';

const logger = createLogger({ marketplace: 'lazada', level: process.env.LOG_LEVEL ?? 'info' });

async function main() {
    const engine = new ScenarioEngine({ logger, marketplace: 'lazada' });
    const here = dirname(fileURLToPath(import.meta.url));
    await engine.loadDirectory(join(here, 'scenarios'));

    const dispatcher = new WebhookDispatcher({
        engine,
        signer: lazadaWebhookSigner({ secret: SECRET }),
        logger,
    });

    const app = express();
    app.disable('x-powered-by');
    app.use(express.json({ limit: '1mb' }));
    app.use(createRequestLogger({ logger }));

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
        res.status(200).json({ status: 'ok', service: 'lazada-mock', version: VERSION });
    });

    app.use('/control', createControlPlaneRouter({ engine, dispatcher, logger }));

    mountLazadaRoutes({ app, secret: SECRET, logger });

    app.use((err, _req, res, _next) => {
        logger.error({ err: err?.message, stack: err?.stack }, 'unhandled error');
        res.status(500).json({ code: '500', type: 'SYSTEM', message: 'internal_error', request_id: 'mock-lazada-error' });
    });

    app.listen(PORT, () => {
        logger.info({ port: PORT, scenarios: engine.listNames() }, 'lazada-mock listening');
    });
}

main().catch((err) => {
    logger.error({ err: err?.message, stack: err?.stack }, 'lazada-mock failed to start');
    process.exit(1);
});
