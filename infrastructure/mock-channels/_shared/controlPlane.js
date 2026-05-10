// Express router for /control/* — identical for both mock servers. The router takes
// a ScenarioEngine and a WebhookDispatcher; it never knows which marketplace it is
// serving (that is the dispatcher's concern via its injected signer).

import { Router } from 'express';

export function createControlPlaneRouter({ engine, dispatcher, logger }) {
    const router = Router();

    router.post('/scenario/:name/start', (req, res) => {
        const { name } = req.params;
        const result = engine.start(name);
        if (!result) {
            res.status(404).json({
                error: 'unknown_scenario',
                name,
                available: engine.listNames(),
            });
            return;
        }
        res.status(200).json({
            active: result.name,
            startedAt: new Date(result.startedAt).toISOString(),
        });
    });

    router.post('/scenario/stop', (_req, res) => {
        engine.stop();
        res.status(200).json({ active: null });
    });

    router.get('/state', (_req, res) => {
        res.status(200).json(engine.state());
    });

    router.get('/scenarios', (_req, res) => {
        res.status(200).json({ scenarios: engine.listNames() });
    });

    router.post('/webhook/register', (req, res) => {
        const { target, events } = req.body ?? {};
        if (typeof target !== 'string' || !/^https?:\/\//i.test(target)) {
            res.status(400).json({ error: 'invalid_target', detail: 'target must be an http(s) URL string' });
            return;
        }
        if (!Array.isArray(events) || events.length === 0 || events.some((e) => typeof e !== 'string')) {
            res.status(400).json({ error: 'invalid_events', detail: 'events must be a non-empty array of strings' });
            return;
        }
        const id = dispatcher.register(target, events);
        logger.info({ id, target, events }, 'registered webhook target');
        res.status(200).json({ id, target, events });
    });

    router.post('/webhook/deliver', async (req, res) => {
        const { event, payload } = req.body ?? {};
        if (typeof event !== 'string' || event.length === 0) {
            res.status(400).json({ error: 'invalid_event' });
            return;
        }
        if (payload === null || typeof payload !== 'object') {
            res.status(400).json({ error: 'invalid_payload' });
            return;
        }
        const result = await dispatcher.deliver(event, payload);
        res.status(200).json(result);
    });

    return router;
}
