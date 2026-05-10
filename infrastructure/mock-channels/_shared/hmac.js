// HMAC-SHA256 helpers shared by both mock servers.
//
// Per `02-technical-design-document.md.docx` §9.4 (signature timing rules), comparison
// MUST be constant-time to defeat timing oracles. We use `crypto.timingSafeEqual` for that.
//
// The marketplace-specific canonical-string construction (Shopee: `partner_id|api_path|timestamp|access_token|shop_id`;
// Lazada: sorted query-string concatenation) lives in shopee-mock/shopee.js and lazada-mock/lazada.js.
// This module is canonicalization-agnostic: callers pass in the canonical string they want signed.

import { createHmac, timingSafeEqual } from 'node:crypto';

/**
 * Compute HMAC-SHA256 of `canonicalString` with `secret`.
 * @param {string} secret  Marketplace partner secret.
 * @param {string} canonicalString  The marketplace-specific canonical request string.
 * @param {'hex'|'base64'} encoding  Output encoding. Shopee uses hex; Lazada uses hex too.
 * @returns {string}
 */
export function computeHmacSha256(secret, canonicalString, encoding = 'hex') {
    if (typeof secret !== 'string' || secret.length === 0) {
        throw new TypeError('secret must be a non-empty string');
    }
    if (typeof canonicalString !== 'string') {
        throw new TypeError('canonicalString must be a string');
    }
    return createHmac('sha256', secret).update(canonicalString, 'utf8').digest(encoding);
}

/**
 * Constant-time hex-string comparison. Returns false on length mismatch
 * without leaking length via early-return (Buffer.from + length-mask).
 */
export function timingSafeEqualHex(a, b) {
    return timingSafeEqualEncoded(a, b, 'hex');
}

/**
 * Constant-time base64-string comparison.
 */
export function timingSafeEqualBase64(a, b) {
    return timingSafeEqualEncoded(a, b, 'base64');
}

function timingSafeEqualEncoded(a, b, encoding) {
    if (typeof a !== 'string' || typeof b !== 'string') return false;
    let bufA;
    let bufB;
    try {
        bufA = Buffer.from(a, encoding);
        bufB = Buffer.from(b, encoding);
    } catch {
        return false;
    }
    if (bufA.length !== bufB.length) {
        // Still run a comparison against a same-length zero buffer to keep timing flat.
        const padding = Buffer.alloc(bufA.length);
        timingSafeEqual(bufA, padding);
        return false;
    }
    return timingSafeEqual(bufA, bufB);
}
