# Mock-channel servers: `_shared/` carries everything that isn't marketplace-specific

**Date**: 2026-05-10
**Affects**: [`infrastructure/mock-channels/`](../../infrastructure/mock-channels/) (U7 deliverable)

## Problem

A mock-channel server stack for two marketplaces (Shopee + Lazada) is the second-most likely
place in the repo to grow copy-paste duplication, after CSPROJ scaffolding. Both servers need
the same pieces:

- HMAC-SHA256 helper with constant-time comparison.
- A scenario engine that loads YAML, validates against an AJV schema, applies response rules
  to incoming requests, and resolves webhook delivery rules.
- A control-plane router that exposes `/control/scenario/*`, `/control/webhook/*`, `/control/state`.
- A webhook dispatcher that signs payloads and respects scenario rules (deliveryCount, gapMs,
  signatureMode).
- A Pino-based request logger.

If we author each of those twice — once in `shopee-mock/`, once in `lazada-mock/` — the two
will drift the moment a third marketplace lands or a scenario rule grows. The user explicitly
flagged this on the U7 turn ("emphasis on architectural consistency").

## Root cause

Express + Node 22 has no built-in concept of a workspace; without a deliberate decision the
default mode is "two folders, two `index.js`, two of everything." The authoring path of least
resistance is duplication.

## Solution

Three folders under `infrastructure/mock-channels/`:

```
_shared/        marketplace-agnostic library (AJV schema, scenario engine, control plane router,
                webhook dispatcher, HMAC helper, Pino request logger)
shopee-mock/    only the Shopee-specific signing canonicalization, endpoint shapes, webhook headers
lazada-mock/    only the Lazada-specific signing canonicalization, endpoint shapes, webhook headers
```

Both server `package.json`s declare the shared library via a local `file:` reference:

```json
"@shopflow/mock-channels-shared": "file:../_shared"
```

`_shared/package.json` is `"private": true` — never published, never versioned externally.
`type: "module"` everywhere; no TypeScript (the surface is small enough that plain Node is the
right scope).

The injection seams that keep the shared layer marketplace-agnostic are:

- `WebhookDispatcher` takes a `signer` callback. Shopee passes `shopeeWebhookSigner({ secret })`,
  Lazada passes `lazadaWebhookSigner({ secret })`. The dispatcher itself never knows about
  Shopee or Lazada.
- `ScenarioEngine.maybeApply(req, res)` writes responses based on regex matchers — those
  matchers are expressed in the YAML, not in code, so the same engine drives both servers.
- `createControlPlaneRouter({ engine, dispatcher, logger })` takes the engine and dispatcher
  by reference; nothing inside it knows the marketplace.

## Prevention

- **Per-marketplace files only contain what the marketplace dictates.** Three things and only
  three things: signature canonicalization, endpoint paths/payloads, webhook header names.
  Anything that fits "the same on every marketplace" belongs in `_shared/`.
- **The Dockerfile build context is `infrastructure/mock-channels/`**, not the per-server
  folder. This is non-obvious — the Dockerfile sits inside `shopee-mock/` but references
  `../_shared/` relatively. The header comment in each Dockerfile spells out the build command
  so the next developer doesn't run `docker build infrastructure/mock-channels/shopee-mock`
  (which would fail to find `_shared/`).
- **Adding a third marketplace** (TikTok Shop, Shopify) is now: add `tiktok-mock/` (3 files —
  `index.js`, `tiktok.js`, `Dockerfile`) + scenarios. Do NOT touch `_shared/` unless something
  genuinely cross-cutting comes up.

## References

- [`infrastructure/mock-channels/README.md`](../../infrastructure/mock-channels/README.md) — repo-level architecture overview.
- [`infrastructure/mock-channels/_shared/scenarioSchema.js`](../../infrastructure/mock-channels/_shared/scenarioSchema.js) — the AJV contract that prevents YAML drift across marketplaces.
- AGENTS.md §1 (working stance: in-tree, machine-agnostic), §11 (module shape canon — note: this is the `.NET` canon; Node mock servers are a documented exception that follow this pattern instead).
- 01-product-development-plan.md.docx §348 ("the mocking IS the engineering").
- 02-technical-design-document.md.docx §9 (Webhook Ingest), §9.4 (signature timing — drives the constant-time comparison in `_shared/hmac.js`).
