// WebhookDispatcher — sends webhook payloads to registered targets, signed with the
// marketplace-specific HMAC. The marketplace-specific signing logic is injected via
// the `signer` callback so this module stays marketplace-agnostic.
//
// Honors the active scenario's webhookDeliveryRules:
//   - deliveryCount > 1   : redelivers the same payload that many times
//   - gapMs               : sleeps between deliveries
//   - signatureMode       : 'valid' | 'clock-skew-3min' | 'wrong-secret' | 'missing'
//
// Uses the global fetch (Node 22 ships it).

const CLOCK_SKEW_OFFSET_MS = 3 * 60 * 1000;

export class WebhookDispatcher {
    /**
     * @param {{ engine: any, signer: (payloadBytes: Buffer, opts: { event: string, timestampMs: number, mode: string }) => { headers: Record<string,string> }, logger: any }} deps
     */
    constructor({ engine, signer, logger }) {
        this.engine = engine;
        this.signer = signer;
        this.logger = logger;
        this.targets = new Map();   // id -> { target, events }
        this._nextId = 1;
    }

    register(target, events) {
        const id = `wh_${this._nextId++}`;
        this.targets.set(id, { target, events: new Set(events) });
        return id;
    }

    /**
     * Manually trigger a webhook delivery. Returns a summary object describing
     * which targets were dispatched to, how many times each, and any errors.
     */
    async deliver(event, payload) {
        const rule = this.engine.resolveWebhookDelivery(event);
        const payloadBytes = Buffer.from(JSON.stringify(payload), 'utf8');
        const matched = [...this.targets.entries()].filter(
            ([, t]) => t.events.has(event) || t.events.has('*'),
        );
        const deliveries = [];
        for (const [id, t] of matched) {
            for (let attempt = 0; attempt < rule.deliveryCount; attempt += 1) {
                if (attempt > 0 && rule.gapMs > 0) {
                    await sleep(rule.gapMs);
                }
                const timestampMs = rule.signatureMode === 'clock-skew-3min'
                    ? Date.now() + CLOCK_SKEW_OFFSET_MS
                    : Date.now();
                const signed = this.signer(payloadBytes, {
                    event,
                    timestampMs,
                    mode: rule.signatureMode,
                });
                const headers = {
                    'Content-Type': 'application/json',
                    ...signed.headers,
                };
                let status = 0;
                let error = null;
                try {
                    const response = await fetch(t.target, {
                        method: 'POST',
                        headers,
                        body: payloadBytes,
                    });
                    status = response.status;
                } catch (err) {
                    error = err?.message ?? String(err);
                }
                deliveries.push({ id, target: t.target, attempt: attempt + 1, status, error });
                this.logger.info(
                    { id, target: t.target, event, attempt: attempt + 1, status, error, mode: rule.signatureMode },
                    'webhook delivered',
                );
            }
        }
        return {
            event,
            rule,
            matchedTargets: matched.length,
            deliveries,
        };
    }
}

function sleep(ms) {
    return new Promise((resolve) => setTimeout(resolve, ms));
}
