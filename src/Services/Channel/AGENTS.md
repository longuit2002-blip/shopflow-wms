# AGENTS.md — Channel module deltas

Per root AGENTS.md §11.82 this file captures Channel-specific invariants only.

## Hard "do not simplify"

- **Outbound API calls carry an idempotency key** per AGENTS.md §6.41. Retries reuse the key. Do not skip the key on "simple" GETs that the marketplace docs claim are safe — they're not, in practice.
- **Mock servers, not in-process mocks** for marketplace integration tests per AGENTS.md §8.59. The mock server lives in `tools/mocks/{shopee,lazada}` (Phase-2 Sprint-4) and matches the real shapes byte-for-byte.
- **Per-channel token bucket + coalescing buffer** is the stock-sync engine pattern per Tech Design v3.0 §5 — do not collapse to "just call the API per change". The pattern protects the marketplaces' rate limits AND our reservation-ledger from sync feedback loops.

## U9 stub state

Schema-only placeholders. The real channel adapter framework + per-channel token bucket lands in Phase-2 Sprint-4. Webhook receivers land in Phase-1 Sprint-2.
