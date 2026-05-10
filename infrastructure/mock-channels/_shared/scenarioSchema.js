// AJV schema for the YAML scenario contract. Both mock servers validate every loaded
// scenario against this schema at startup; malformed scenarios cause a hard refusal-to-start
// rather than a silent skip. The contract lives here (one place) so Shopee and Lazada
// scenarios cannot drift.

import Ajv from 'ajv';

export const scenarioSchema = {
    $id: 'https://shopflow.local/schemas/mock-channel-scenario.json',
    type: 'object',
    additionalProperties: false,
    required: ['name', 'description', 'behavior'],
    properties: {
        name: {
            type: 'string',
            pattern: '^[a-z0-9][a-z0-9-]{1,63}$',
        },
        description: {
            type: 'string',
            minLength: 1,
            maxLength: 1000,
        },
        behavior: {
            type: 'object',
            additionalProperties: false,
            required: ['responses', 'webhookDeliveryRules'],
            properties: {
                responses: {
                    type: 'array',
                    items: {
                        type: 'object',
                        additionalProperties: false,
                        required: ['matchPath', 'matchMethod', 'returnStatus'],
                        properties: {
                            matchPath: { type: 'string', minLength: 1 },
                            matchMethod: {
                                type: 'string',
                                enum: ['*', 'GET', 'POST', 'PUT', 'PATCH', 'DELETE'],
                            },
                            returnStatus: { type: 'integer', minimum: 100, maximum: 599 },
                            returnHeaders: {
                                type: 'object',
                                additionalProperties: { type: 'string' },
                            },
                            returnBody: { type: 'string' },
                            // Special-cased non-HTTP behaviours. The scenario engine knows about these.
                            //   "until-stopped"    : applies to every matching request until /control/scenario/stop
                            //   integer            : applies to the next N matching requests
                            //   "partial-then-eof" : write the first half of returnBody then destroy the socket
                            repeat: {
                                oneOf: [
                                    { type: 'string', enum: ['until-stopped', 'partial-then-eof'] },
                                    { type: 'integer', minimum: 1, maximum: 100000 },
                                ],
                            },
                            // Time-bounded scenarios (e.g. 5xx-burst-30s).
                            durationMs: { type: 'integer', minimum: 1, maximum: 600000 },
                        },
                    },
                },
                webhookDeliveryRules: {
                    type: 'array',
                    items: {
                        type: 'object',
                        additionalProperties: false,
                        required: ['eventPattern', 'deliveryCount'],
                        properties: {
                            eventPattern: { type: 'string', minLength: 1 },
                            deliveryCount: { type: 'integer', minimum: 1, maximum: 50 },
                            gapMs: { type: 'integer', minimum: 0, maximum: 600000 },
                            signatureMode: {
                                type: 'string',
                                enum: ['valid', 'clock-skew-3min', 'wrong-secret', 'missing'],
                            },
                        },
                    },
                },
            },
        },
    },
};

const ajv = new Ajv({ allErrors: true, strict: true });
const validator = ajv.compile(scenarioSchema);

/**
 * Validate a parsed scenario object against `scenarioSchema`.
 * @returns {{ valid: boolean, errors: Array<object>|null }}
 */
export function validateScenario(scenarioObject) {
    const ok = validator(scenarioObject);
    return { valid: !!ok, errors: ok ? null : (validator.errors ?? []) };
}
