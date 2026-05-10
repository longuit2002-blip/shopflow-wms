// Pino-based request logger shared by both mock servers.
// Emits structured JSON with marketplace, method, path, status, duration_ms.

import pino from 'pino';
import pinoHttp from 'pino-http';

export function createLogger({ marketplace, level = 'info' }) {
    return pino({
        level,
        base: { service: `${marketplace}-mock` },
        timestamp: pino.stdTimeFunctions.isoTime,
    });
}

export function createRequestLogger({ logger }) {
    return pinoHttp({
        logger,
        customLogLevel(_req, res, err) {
            if (err || res.statusCode >= 500) return 'error';
            if (res.statusCode >= 400) return 'warn';
            return 'info';
        },
        customSuccessMessage(req, res) {
            return `${req.method} ${req.url} -> ${res.statusCode}`;
        },
    });
}
