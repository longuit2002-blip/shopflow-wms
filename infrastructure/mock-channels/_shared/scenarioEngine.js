// ScenarioEngine — loads scenarios/*.yml at startup, validates them, and applies the
// active scenario's response rules to incoming HTTP requests. It is deliberately
// shared between Shopee and Lazada: per-marketplace differences are confined to which
// scenario directory is loaded and which signature canonicalization the server passes
// down to the webhook dispatcher.
//
// State model:
//   active: { name, scenario, startedAt: number(ms), responseRepeats: Map<index, remaining> }
//
// Lifecycle:
//   1. Server boot → engine.loadDirectory(scenariosDir). Validates every YAML; throws on first failure.
//   2. POST /control/scenario/{name}/start  → engine.start(name).
//   3. Each request hits engine.maybeApply(req, res). If a response rule matches, the engine writes
//      the response (including the partial-then-eof socket trick) and returns true; the route handler
//      should bail out so it does not also write a body.
//   4. POST /control/scenario/stop → engine.stop().
//   5. POST /control/webhook/deliver → engine.resolveWebhookDelivery(eventName) returns the rule
//      that applies (or the default single-delivery rule if no scenario is active).

import { readdir, readFile } from 'node:fs/promises';
import { join } from 'node:path';
import yaml from 'js-yaml';
import { validateScenario } from './scenarioSchema.js';

export class ScenarioEngine {
    constructor({ logger, marketplace }) {
        this.logger = logger;
        this.marketplace = marketplace;
        this.scenarios = new Map();   // name -> validated scenario object
        this.active = null;
    }

    async loadDirectory(scenariosDir) {
        const files = (await readdir(scenariosDir)).filter((f) => f.endsWith('.yml') || f.endsWith('.yaml'));
        if (files.length === 0) {
            throw new Error(`scenario directory ${scenariosDir} has no .yml files`);
        }
        for (const file of files) {
            const fullPath = join(scenariosDir, file);
            const raw = await readFile(fullPath, 'utf8');
            let parsed;
            try {
                parsed = yaml.load(raw);
            } catch (err) {
                throw new Error(`scenario ${file} failed YAML parse: ${err.message}`);
            }
            const { valid, errors } = validateScenario(parsed);
            if (!valid) {
                const detail = (errors ?? []).map((e) => `${e.instancePath} ${e.message}`).join('; ');
                throw new Error(`scenario ${file} failed schema validation: ${detail}`);
            }
            if (this.scenarios.has(parsed.name)) {
                throw new Error(`scenario name '${parsed.name}' is duplicated across files`);
            }
            this.scenarios.set(parsed.name, parsed);
            this.logger.info({ marketplace: this.marketplace, scenario: parsed.name }, 'loaded scenario');
        }
    }

    listNames() {
        return [...this.scenarios.keys()].sort();
    }

    start(name) {
        const scenario = this.scenarios.get(name);
        if (!scenario) return null;
        const responseRepeats = new Map();
        for (let i = 0; i < (scenario.behavior.responses ?? []).length; i += 1) {
            const rule = scenario.behavior.responses[i];
            if (typeof rule.repeat === 'number') {
                responseRepeats.set(i, rule.repeat);
            }
        }
        this.active = {
            name,
            scenario,
            startedAt: Date.now(),
            responseRepeats,
        };
        this.logger.info({ marketplace: this.marketplace, scenario: name }, 'scenario started');
        return this.active;
    }

    stop() {
        const previous = this.active?.name ?? null;
        this.active = null;
        if (previous) {
            this.logger.info({ marketplace: this.marketplace, scenario: previous }, 'scenario stopped');
        }
        return previous;
    }

    state() {
        if (!this.active) return { active: null, sinceMs: 0 };
        return { active: this.active.name, sinceMs: Date.now() - this.active.startedAt };
    }

    /**
     * Try to apply the active scenario to the incoming request.
     * Returns true if a response was written (caller must not write more).
     */
    maybeApply(req, res) {
        if (!this.active) return false;
        const rules = this.active.scenario.behavior.responses ?? [];
        for (let i = 0; i < rules.length; i += 1) {
            const rule = rules[i];
            if (!matchesRule(rule, req)) continue;
            if (rule.durationMs && Date.now() - this.active.startedAt > rule.durationMs) continue;
            if (typeof rule.repeat === 'number') {
                const remaining = this.active.responseRepeats.get(i) ?? 0;
                if (remaining <= 0) continue;
                this.active.responseRepeats.set(i, remaining - 1);
            }
            this._writeRuleResponse(rule, res);
            return true;
        }
        return false;
    }

    _writeRuleResponse(rule, res) {
        const headers = rule.returnHeaders ?? {};
        for (const [k, v] of Object.entries(headers)) {
            res.setHeader(k, v);
        }
        res.statusCode = rule.returnStatus;
        const body = rule.returnBody ?? '';
        if (rule.repeat === 'partial-then-eof') {
            // Write only the first half of the body, flush, then destroy the socket.
            // This exercises consumer-side partial-response handling.
            const half = Math.max(1, Math.floor(body.length / 2));
            res.write(body.slice(0, half));
            // Force a flush, then tear down the connection without a clean end().
            const socket = res.socket;
            setImmediate(() => {
                if (socket && !socket.destroyed) socket.destroy();
            });
            return;
        }
        res.end(body);
    }

    /**
     * Returns the webhook delivery rule that applies for the given event name.
     * If no scenario is active or no rule matches, returns the implicit default
     * (one delivery, valid signature, zero gap).
     */
    resolveWebhookDelivery(eventName) {
        const defaultRule = { eventPattern: '*', deliveryCount: 1, gapMs: 0, signatureMode: 'valid' };
        if (!this.active) return defaultRule;
        const rules = this.active.scenario.behavior.webhookDeliveryRules ?? [];
        for (const rule of rules) {
            if (rule.eventPattern === '*' || rule.eventPattern === eventName) {
                return { gapMs: 0, signatureMode: 'valid', ...rule };
            }
        }
        return defaultRule;
    }
}

function matchesRule(rule, req) {
    if (rule.matchMethod !== '*' && rule.matchMethod !== req.method) return false;
    let regex;
    try {
        regex = new RegExp(rule.matchPath);
    } catch {
        return false;
    }
    return regex.test(req.path);
}
