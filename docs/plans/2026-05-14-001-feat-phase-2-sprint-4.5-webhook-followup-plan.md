---
title: "feat: Phase-2 Sprint-4.5 — webhook receiver follow-up + scale-gate harness"
type: feat
status: completed
date: 2026-05-14
completed: 2026-05-15
origin: docs/brainstorms/2026-05-14-sprint-4.5-webhook-followup-requirements.md
follows: docs/phase-gates/2026-05-13-sprint-4-signoff.md
signoff: docs/phase-gates/2026-05-15-sprint-4.5-signoff.md
tag: v0.6.1-sprint-4.5
---

# feat: Phase-2 Sprint-4.5 — webhook receiver follow-up + scale-gate harness

## Overview

Close the four Sprint-4 sign-off deferrals as a single ~1-week closure unit. The Channel webhook ingress pipeline is currently wired end-to-end *structurally* but the load-bearing semantics are stubbed: `provider_event_id` derives from a body-hash instead of the parsed Shopee envelope, the outbox row carries a placeholder event-type string instead of the canonical `OrderImportedV1` contract, and the headline multi-tenant fairness scale gate has three `Skip`'d slots with no harness body. Sprint-4.5 wires all four into their real shapes and tags `v0.6.1-sprint-4.5` so Sprint-5 (Stock Sync Engine) starts on a foundation where the inbound → `OrderImportedV1` → saga round-trip is exercised at unit AND scale-gate level.

This plan cuts a fresh branch from `v0.6.0-sprint-4`. The Sprint-4 branch (`feat/phase-2-sprint-4-channel-webhook`) is preserved as the source of the deferred-item list.

---

## Problem Frame

Per the Sprint-4 sign-off ([`docs/phase-gates/2026-05-13-sprint-4-signoff.md`](../phase-gates/2026-05-13-sprint-4-signoff.md)), four items shipped as deferrals:

1. **`WebhooksController.ExtractProviderEventIdStub`** derives the idempotency key from `SHA256(body || signature)`. Replays of the *same byte sequence* are caught by the existing `UNIQUE(channel_id, provider_event_id)` index, but a marketplace re-emitting the same event with even a re-signed body (or any byte-level drift the marketplace permits) would NOT be caught — the idempotency anchor is structurally wrong.
2. The first-write outbox row carries `event_type = "ShopFlow.Channel.Webhooks.WebhookReceivedV1"` as a placeholder string. The Outbound `OrderImportedConsumer` (Sprint-4 U8) is wired and waiting; the producer side simply hasn't been swapped over. No part of the receiver currently produces the `OrderImportedV1` contract.
3. `MultiTenantWebhookScaleGateTests` ships as three `Skip`'d `Category=Load` slots ([`tests/ShopFlow.Channel.IntegrationTests/MultiTenantWebhookScaleGateTests.cs`](../../tests/ShopFlow.Channel.IntegrationTests/MultiTenantWebhookScaleGateTests.cs)). The headline noisy-neighbor assertions (p99 < 200ms, per-tenant fairness ≥ 0.85 under burst, replay-100×-equals-1, cross-tenant signature → 401) have no harness body and therefore no measurement.
4. No local runtime smoke of the Aspire mock-server wiring has happened — Docker daemon was not running during Sprint-4. CI is the planned-canonical validation, but the local round-trip remains unverified.

These compound: any Sprint-5 scale test that exercises the inbound side measures the wrong shape until (1) and (2) close. The receiver-side fairness floor itself is unmeasured until (3) lands.

---

## Requirements Trace

Origin requirements (`docs/brainstorms/2026-05-14-sprint-4.5-webhook-followup-requirements.md`) → U-IDs:

| R-ID | Requirement | Owning U-IDs |
|---|---|---|
| R1 | Controller routes through `IChannelAdapterFactory.ResolveFor(channelType)` → adapter parser → real `provider_event_id` | U2 |
| R2 | Parser failures surface as 400 with stable error code; 401/404/200 paths unchanged | U2 |
| R3 | UNIQUE-23505 idempotency catch continues to work; source of `provider_event_id` changes, mechanism unchanged | U2 |
| R4 | Outbox row carries `OrderImportedV1` (assembly-qualified type name + parsed contract) instead of placeholder string | U3 |
| R5 | Each line's `InternalSku` resolved through `IProductMappingService` (Exact → Levenshtein @ 0.6 → null) | U3 (orchestrator-side; per-line lookup) |
| R6 | **Corrected vs origin** — per canon (`src/Shared/ShopFlow.Contracts/Channel/OrderImportedV1.cs` doc): unmapped lines fail the whole import (`webhook_events.status = Failed`, no `OrderImportedV1` emitted). `OrderImportedLineV1.Sku` is non-nullable; the brainstorm's "emit with `InternalSku=null`" was structurally impossible against the existing contract | U3 |
| R7 | `TenantWebhookHarness` reusable integration helper: multi-tenant provisioning + signed-post sender | U4 |
| R8 | Three `Skip`'d scale-gate tests get real bodies | U5 |
| R9 | Harness uses Testcontainers Postgres + real controller pipeline; `Category=Load` tagging preserved | U4, U5 |
| R10 | Runtime smoke via `task up` + Shopee mock when Docker available; documented deferral otherwise | U6 |
| R11 | Sign-off doc + tag `v0.6.1-sprint-4.5` + CHANGELOG + README/CLAUDE.md "Current stage" updates + plan status → completed | U6 |

Origin acceptance examples AE1-AE5 carry forward as integration tests in U2, U3, and U5 (specific links in each unit's `Test scenarios`).

---

## Scope Boundaries

### In scope

- All R1-R11 (R6 corrected per canon).
- Branch `feat/phase-2-sprint-4.5-webhook-followup` cut from `v0.6.0-sprint-4`.
- Sprint-4.5 sign-off doc + annotated tag `v0.6.1-sprint-4.5`.

### Deferred to Follow-Up Work

- **Per-event-type policy for non-`order.created` webhooks** (e.g., `order.cancelled`, `shipment.updated`). Sprint-4.5 emits `OrderImportedV1` only when `WebhookEnvelope.EventType == "order.created"`; other event types persist the `webhook_events` row but emit no outbox row. The full per-event-type policy table is Sprint-6+ work alongside Lazada.
- **Outbound `OrderImportedConsumer` refinements** based on what the real (non-placeholder) contract round-trip surfaces — additions land alongside their discoveries, not pre-emptively.

### Out of scope (Sprint-5+ / Phase-2 / Phase-3)

- **Sprint-5 Stock Sync Engine** — `ShopeeAdapter.PushStockUpdateAsync` body remains a stub.
- **Sprint-6 Lazada adapter + oversell compensation**.
- **New Channel schema changes / migrations** — none. Sprint-4 U1-U2 schema covers everything Sprint-4.5 emits.
- **Read-side projections / streaming-aware mapping cursor** (Sprint-5+).
- **CSharpier formatting cleanup of the 23 pre-existing drift files** — separate Phase-2-cleanup commit, not Sprint-4.5.
- **Webhook receiver auth hardening** (IP allowlist, per-channel header allowlist) — Phase-2 hardening.
- **Multi-instance dispatcher leader election** — Phase-2 nice-to-have.
- **`IChannelAdapter` framework re-shape** — Sprint-4 ships the shape. Sprint-4.5 *extends* it with one new method (order-shape extraction, U1) but does not redesign.

### Deferred to Implementation

- Exact wire format / property names for the structured warning log on parse failure or mapping failure. Serilog destructuring conventions inherited.
- Wall-time threshold for fairness measurement on the GitHub Actions runner vs the dev machine. First nightly CI run lands the number.
- Whether mapping resolution batches per webhook (one `ResolveBatchAsync(channelId, externalSkus)`) or stays per-line. U3 ships per-line per the existing port shape; if R8 measurements show the per-line round-trip is a hotspot, the consumer-side batch becomes the fast follow-up.
- Specific assertion shape for the "saga reaches a state past `New`" runtime smoke (R10) — pollers / direct queries / observed side-effects all viable; U6 picks based on Sprint-3-redux's saga harness patterns.

---

## Context & Research

### Relevant code (Sprint-4 anchor points to extend)

- [`src/Services/Channel/ShopFlow.Channel.Api/Controllers/WebhooksController.cs`](../../src/Services/Channel/ShopFlow.Channel.Api/Controllers/WebhooksController.cs) — current receiver entry; `ExtractProviderEventIdStub` is the swap-out point.
- [`src/Services/Channel/ShopFlow.Channel.Application/Adapters/IChannelAdapter.cs`](../../src/Services/Channel/ShopFlow.Channel.Application/Adapters/IChannelAdapter.cs) — `ParseWebhook` returns envelope-level metadata only; Sprint-4.5 adds order-shape extraction.
- [`src/Services/Channel/ShopFlow.Channel.Infrastructure/Adapters/ShopeeAdapter.cs`](../../src/Services/Channel/ShopFlow.Channel.Infrastructure/Adapters/ShopeeAdapter.cs) + [`ShopeeWebhookParser.cs`](../../src/Services/Channel/ShopFlow.Channel.Infrastructure/Adapters/ShopeeWebhookParser.cs) — current parser reads envelope fields (event_id, event_type, timestamp) but ignores the `data` payload (kept as `JsonElement`). Sprint-4.5 adds the second-pass extraction of `data` → order shape.
- [`src/Services/Channel/ShopFlow.Channel.Application/Webhooks/IngestWebhookService.cs`](../../src/Services/Channel/ShopFlow.Channel.Application/Webhooks/IngestWebhookService.cs) — orchestrator already takes `downstreamEventType` and `downstreamPayload` as parameters; the swap-in point for `OrderImportedV1` is its caller (controller).
- [`src/Services/Channel/ShopFlow.Channel.Application/Ports/IProductMappingService.cs`](../../src/Services/Channel/ShopFlow.Channel.Application/Ports/IProductMappingService.cs) — returns `ProductMappingResolution?` (null on miss); per-line lookup at receive time.
- [`src/Shared/ShopFlow.Contracts/Channel/OrderImportedV1.cs`](../../src/Shared/ShopFlow.Contracts/Channel/OrderImportedV1.cs) — canon contract: `(OrderId Guid, TenantId Guid, ChannelId Guid, ChannelExternalOrderId string, ShippingProfile string, Lines IReadOnlyList<OrderImportedLineV1>, OccurredAt DateTime)`. `OrderImportedLineV1.Sku` is non-nullable.
- [`tests/ShopFlow.Channel.IntegrationTests/MultiTenantWebhookScaleGateTests.cs`](../../tests/ShopFlow.Channel.IntegrationTests/MultiTenantWebhookScaleGateTests.cs) — three Skip'd tests; bodies land in U5.
- [`tests/ShopFlow.SharedKernel.IntegrationTests/PostgresFixture.cs`](../../tests/ShopFlow.SharedKernel.IntegrationTests/PostgresFixture.cs) — Testcontainers fixture pattern; U4's `TenantWebhookHarness` borrows the lifecycle shape.
- [`tools/mocks/shopee/`](../../tools/mocks/shopee/) — Shopee mock server already exposes `POST /__send-webhook` (Sprint-4 U7); U6's runtime smoke uses it.

### Carried-forward institutional learnings

- `docs/solutions/2026-05-13-multi-row-cte-predicate-must-live-in-update.md` — the Sprint-3-redux K11 fix is not load-bearing here (Sprint-4.5 doesn't touch reservation-ledger CTEs), but the principle (predicate placement matters) applies to mapping-batch SQL if U3 surfaces it.
- `docs/solutions/2026-05-13-cross-module-outbox-table-name-collision.md` — the Sprint-2.5 module-prefix rename is the precedent for the `channel_outbox_messages` table Sprint-4 ships; U3's outbox emit lands in that table, not the legacy unprefixed name.
- The Sprint-1-redux / Sprint-3-redux / Sprint-4 pattern: scale-gate tests tagged `Category=Load`, harness body written to assert per-tenant invariants, wall-time measurement deferred to CI when local Docker is absent. U4-U5 inherit this posture.

### External references

None. Sprint-4 already established all patterns; Sprint-4.5 is wiring + harness, no new domain to research.

---

## Key Technical Decisions

| Decision | Rationale |
|---|---|
| **R6 reversal — fail whole import on any unmapped line** | The `OrderImportedV1` contract's `OrderImportedLineV1.Sku` is non-nullable, AND the contract doc comment explicitly states: *"Unmappable lines fail the whole import at the receiver per Sprint-4 plan Open Questions (status set to Failed on the webhook_events row, no OrderImportedV1 emitted)."* The brainstorm's R6 (emit-with-null) was structurally impossible against canon. Plan corrects to fail-whole-import. The `webhook_events.status = Failed` row + the raw payload remains for operator-side replay after a manual `IProductMappingRepository.UpsertManualAsync`. |
| **Extend `IChannelAdapter` with one new method, `ParseOrderCreated`, rather than re-shape the interface** | Sprint-4's docstring already calls out "two responsibilities only" but the second was a Sprint-5 stub; adding a third for order-shape extraction is the smallest viable surface. Alternative considered: dedicated per-channel `IOrderEnvelopeParser` (rejected — duplicates the per-marketplace knowledge the adapter already owns). The Lazada adapter in Sprint-6 implements the same method against Lazada's order shape. |
| **`ParseOrderCreated` returns a Channel-internal `ExternalOrderDraft`, NOT `OrderImportedV1` directly** | Adapter knows marketplace shapes (external SKUs, raw line items) but should not know about `IProductMappingService` or `OrderImportedV1`'s internal-sku shape. The orchestrator (U3) sits between draft and contract, owning mapping resolution + final assembly. Keeps the Domain → Application → Infrastructure layering AGENTS.md §2 prescribes. |
| **Event-type gating** | `OrderImportedV1` is only emitted when `WebhookEnvelope.EventType == "order.created"`. Other event types (`order.cancelled`, etc.) persist the `webhook_events` row but no outbox row. The full per-event-type policy table is Sprint-6+; Sprint-4.5 ships the explicit gate so future event types add cases rather than re-shape the switch. |
| **Mapping resolution is per-line (not batched)** | Existing `IProductMappingService.ResolveAsync(channelId, externalSku)` is per-line. Plan ships per-line; U5 burst measurements decide whether a batch port is needed. Adding `ResolveBatchAsync` now is YAGNI. |
| **`TenantWebhookHarness` extends — does not replace — `PostgresFixture`** | Sprint-4.5 introduces multi-tenant DB provisioning + channel/secret seeding on top of the existing per-class Testcontainers Postgres. Single container amortises across all U4/U5 tests; harness layer is the multi-tenant + signing-helper add-on. |
| **Point release tag `v0.6.1-sprint-4.5`** | Matches the Sprint-2.5 precedent (`v0.4.1-sprint-2.5`). Sprint-4.5 ships no new capability surface — it closes deferrals — so the minor version doesn't increment. |
| **Branch from `v0.6.0-sprint-4`, not from `feat/phase-2-sprint-4-channel-webhook` HEAD** | "Branch from prior tag" pattern since Sprint-1-redux. Tag is the explicit anchor; branch HEAD may have drifted with WIP. |

---

## Open Questions

### Resolved during planning

- **What replaces the placeholder event-type string?** → `typeof(OrderImportedV1).AssemblyQualifiedName!` written to `channel_outbox_messages.event_type`. Sprint-4 U4's `OutboxRouteRegistry` already routes this type as `SendKind.Send` so the dispatcher does the right thing without further changes.
- **Where does mapping resolution live — receiver or consumer?** → Receiver. Keeps idempotency simple — the outbox row carries the fully-resolved `OrderImportedV1`; replays produce identical payloads. Consumer-side resolution would re-mint mappings on every replay and risk drift if a manual mapping was upserted between the original receive and a replay.
- **What happens when EventType is not `order.created`?** → `webhook_events` row persists with `status=Received` (Sprint-4 default), NO outbox row. Future sprints add per-event-type policies; Sprint-4.5 ships the gate.
- **Should `ExtractProviderEventIdStub` be deleted or kept?** → Deleted. Leaving dead code with `Sprint-4 U3 stub` comments invites future agents to find and "use" it. Removing makes the swap permanent.

### Deferred to Implementation

- Specific log structure (field names, levels) for parse-failed / unmapped-line / event-type-skipped cases — Serilog destructuring conventions in place since Sprint-1-redux are sufficient guidance.
- Whether `TenantWebhookHarness` returns a `HarnessContext` object or exposes services via DI scope — implementer chooses based on what reads cleanest at the call site.
- Exact retry / settle window for the burst measurement — Sprint-3-redux U8's warm-up + `ClearAllPools()` patterns carry forward; specific timings tuned during U5 implementation.

---

## High-Level Technical Design

The post-Sprint-4.5 receiver pipeline:

```
HTTP POST /api/channel/webhooks/{channelType}/{channelId}
  │
  ▼
WebhooksController.Receive(channelType, channelId, body, headers)
  │
  ├─ ChannelDirectory.LookupAsync(channelId)              [Sprint-4]
  │       ├─ 404 if unknown
  │       └─ returns ChannelBinding{ TenantId, ChannelType, SecretEncrypted }
  │
  ├─ SignatureVerifier.Verify(body, signature, secret)    [Sprint-4]
  │       └─ 401 if invalid
  │
  ├─ TenantCatalog.LookupByIdAsync + RequestContext.Bind  [Sprint-4]
  │
  ├─ IChannelAdapterFactory.ResolveFor(channelType)       [Sprint-4]
  │       └─ ShopeeAdapter
  │
  ├─ adapter.ParseWebhook(channelId, body, headers)       [Sprint-4]   ◄── R1 / U2
  │       └─ WebhookEnvelope { ChannelId, ProviderEventId, EventType, RawPayload, OccurredAt }
  │       └─ 400 with stable code if parse fails                             ◄── R2 / U2
  │
  ├─ IF envelope.EventType == "order.created":            [Sprint-4.5 new] ◄── Key Decision: event-type gating
  │   │
  │   ├─ adapter.ParseOrderCreated(envelope)              [Sprint-4.5 new] ◄── R4 / U1
  │   │       └─ ExternalOrderDraft { ChannelExternalOrderId, ShippingProfile, Lines[(ExternalSku, Qty)] }
  │   │       └─ Result.Failure on missing fields → ingestService records status=Failed, returns 400-shape
  │   │
  │   ├─ ResolveAllMappings(channelId, draft.Lines)        [Sprint-4.5 new] ◄── R5 / U3
  │   │       └─ per-line IProductMappingService.ResolveAsync
  │   │       └─ if ANY null → fail whole: WebhookEvent.status=Failed, no outbox, return 200 with import_failed   ◄── R6 / U3
  │   │
  │   └─ Build OrderImportedV1{...} with resolved InternalSkus              ◄── R4 / U3
  │
  ├─ IngestWebhookService.IngestAsync(
  │       envelope,
  │       downstreamEventType: typeof(OrderImportedV1).AssemblyQualifiedName,
  │       downstreamPayload: OrderImportedV1 instance,
  │       ct)                                              [Sprint-4 + 4.5 wiring]
  │       ├─ WebhookEventRepository.TryInsertAsync         [Sprint-4]
  │       │       └─ UNIQUE-23505 catch → IsDuplicate=true  ◄── R3 / U2
  │       └─ ChannelOutbox.AppendAsync (first-write only)
  │
  └─ 200 { eventId, isDuplicate }
```

The diff vs Sprint-4 close: two new adapter-side calls (`ParseOrderCreated`) and one new orchestrator layer (mapping resolution + contract assembly) wrapping `IngestWebhookService.IngestAsync`. The orchestrator can live in the controller for Sprint-4.5 since there's only one event type to gate on; if Sprint-6 adds many event types this becomes a dedicated `IWebhookOrchestrator` service. YAGNI for now.

*This sketch is directional — implementer should treat as context, not code to reproduce.*

---

## Implementation Units

### U1. Adapter order-shape extraction

**Goal:** Extend `IChannelAdapter` with `ParseOrderCreated` returning a Channel-internal `ExternalOrderDraft` (raw external SKUs + quantities, marketplace-side order id, shipping profile). Implement on `ShopeeAdapter` by reading the `data` `JsonElement` from the existing parser.

**Requirements:** R4 (partial — the external-side parse), R6 (depends on this for unmapped-line detection upstream).

**Dependencies:** none.

**Files:**
- Modify: `src/Services/Channel/ShopFlow.Channel.Application/Adapters/IChannelAdapter.cs` — add `ParseOrderCreated(WebhookEnvelope) → Result<ExternalOrderDraft>`.
- Create: `src/Services/Channel/ShopFlow.Channel.Application/Webhooks/ExternalOrderDraft.cs` — `(string ChannelExternalOrderId, string ShippingProfile, IReadOnlyList<ExternalOrderLine> Lines)` + `ExternalOrderLine(string ExternalSku, int Qty)`.
- Modify: `src/Services/Channel/ShopFlow.Channel.Infrastructure/Adapters/ShopeeAdapter.cs` — implement `ParseOrderCreated`.
- Modify: `src/Services/Channel/ShopFlow.Channel.Infrastructure/Adapters/ShopeeWebhookParser.cs` — surface the `data` `JsonElement` (or add a new method that parses `data` into the draft shape). Keep the existing `Parse(channelId, body)` envelope-level contract for backward compatibility; ADD a `ParseOrderData(JsonElement data) → Result<ExternalOrderDraft>` (or equivalent) for the new layer.
- Create: `tests/ShopFlow.Channel.UnitTests/Adapters/ShopeeAdapterParseOrderCreatedTests.cs`.

**Approach:**
- Adapter method delegates to a private helper that reads Shopee's `data` field as `{ "order_sn": "...", "shipping_profile": "...", "item_list": [{ "item_sku": "...", "quantity": N }, ...] }`. Shape derived from `tools/mocks/shopee/` payloads (Sprint-4 U7 + cherry-picked fixtures from `tests/fixtures/channels/shopee/`).
- Failure modes return `Result<ExternalOrderDraft>.Failure(message, "shopee.order.<reason>")` codes — missing `order_sn`, missing `item_list`, empty `item_list`, missing `item_sku` on any line, non-positive `quantity` on any line.
- No mapping resolution here — adapter is marketplace-shape only; orchestrator (U3) consumes the draft.

**Execution note:** Test-first on the failure paths. Write `ParseOrderCreated_MissingOrderSn_ReturnsFailure` red first, then implement.

**Patterns to follow:**
- Existing `ShopeeWebhookParser.Parse` (failure-mode handling with stable error codes).
- `tests/fixtures/channels/shopee/` for realistic Shopee payload shapes.

**Test scenarios** (`tests/ShopFlow.Channel.UnitTests/Adapters/ShopeeAdapterParseOrderCreatedTests.cs`):
- **Happy path**: valid Shopee `data` payload with `order_sn`, `shipping_profile`, 2-line `item_list` → returns `Result.Success` with the corresponding `ExternalOrderDraft`; all fields trimmed; line counts match.
- **Edge — single-line order**: 1-line `item_list` → success, draft has 1 line.
- **Edge — non-`order.created` event type**: if the caller passes a non-`order.created` envelope, behavior is documented (caller is responsible for not invoking; adapter MAY return Failure or Success-but-empty per implementer choice — pick one and stick with it).
- **Error — missing `order_sn`**: returns `Failure` with code `shopee.order.order_sn_required`.
- **Error — empty `item_list`**: returns `Failure` with code `shopee.order.lines_empty`.
- **Error — line missing `item_sku`**: returns `Failure` with code `shopee.order.line_sku_required`.
- **Error — line with `quantity <= 0`**: returns `Failure` with code `shopee.order.line_quantity_invalid`.
- **Error — malformed JSON in `data`**: returns `Failure` with code `shopee.order.data_malformed` (or equivalent).

**Verification:**
- All test cases above pass.
- `dotnet build` clean.
- No new `IChannelAdapter` methods break existing tests in `tests/ShopFlow.Channel.UnitTests/`.

---

### U2. WebhooksController parser wiring

**Goal:** Replace the body-hash `ExtractProviderEventIdStub` with the real adapter-routed parse. Controller resolves the adapter via `IChannelAdapterFactory.ResolveFor(channel.ChannelType)`, calls `adapter.ParseWebhook(channelId, body, headers)`, uses the returned `WebhookEnvelope.ProviderEventId` as the idempotency token.

**Requirements:** R1, R2, R3.

**Dependencies:** none (uses existing Sprint-4 `IChannelAdapterFactory` shape; does not depend on U1's `ParseOrderCreated` extension).

**Files:**
- Modify: `src/Services/Channel/ShopFlow.Channel.Api/Controllers/WebhooksController.cs` — replace `ExtractProviderEventIdStub` call with adapter route; delete the stub method; add `IChannelAdapterFactory` constructor parameter; route parse failures to 400 with stable error code.
- Modify (or extend) integration tests: `tests/ShopFlow.Channel.IntegrationTests/WebhooksControllerTests.cs` (create if not present) for the new wiring assertions.

**Approach:**
- Controller after signature-verify + tenant-bind: `var adapter = _adapterFactory.ResolveFor(binding.ChannelType);` → `var envelopeResult = adapter.ParseWebhook(channelId, bodyBytes, headerDict);` → on Failure return 400 with `code: envelopeResult.ErrorCode`.
- `headerDict`: build `IReadOnlyDictionary<string, string>` from `HttpContext.Request.Headers` (most-recent value if multi-valued).
- Delete `ExtractProviderEventIdStub` static method — leaving it invites future agents to "reuse" the stub.

**Execution note:** Test-first. Add the integration test that asserts the parsed `provider_event_id` flows through to `webhook_events.provider_event_id`, watch it fail against the stub, then swap in the adapter route.

**Patterns to follow:**
- Existing controller actions in `WebhooksController.cs` (Sprint-4 U3).
- `tests/ShopFlow.SharedKernel.IntegrationTests/PostgresFixture.cs` for the Testcontainers setup pattern.

**Test scenarios** (`tests/ShopFlow.Channel.IntegrationTests/WebhooksControllerTests.cs`, tagged `Category=Integration`):
- **Happy path — Covers AE2.** Given a valid Shopee payload with `event_id = "ORDER-2026-12345"` and a valid HMAC, when the controller receives the POST, the `webhook_events` row stores `provider_event_id = "ORDER-2026-12345"` (NOT a hash of body+signature) and the response is `{ isDuplicate: false }`.
- **Replay — Covers AE2.** Same payload + signature POSTed twice 100ms apart, when the parser extracts the same `provider_event_id` from each, the first call returns `{ isDuplicate: false }` and the second returns `{ isDuplicate: true }`; `channel_outbox_messages` contains exactly 1 row.
- **Edge — alternate body, same event id**: payload bytes differ slightly (e.g., key ordering) but the parsed `event_id` is identical → second call returns `{ isDuplicate: true }`. Verifies the source of `provider_event_id` is the parsed field, not the body.
- **Error — Covers AE1.** Payload missing `event_id` → controller returns 400 with code `shopee.event_id_required`; zero rows in `webhook_events`, zero rows in `channel_outbox_messages`.
- **Error — malformed JSON body**: returns 400 with code `shopee.body_malformed`; zero DB writes.
- **Integration — full pipeline pass-through**: existing Sprint-4 happy-path test (signature verify → tenant bind → ingest) continues to pass against the new controller wiring. Run as regression.

**Verification:**
- All scenarios above pass against Testcontainers Postgres.
- `ExtractProviderEventIdStub` no longer exists in tree (`grep -r "ExtractProviderEventIdStub" src/` returns nothing).
- No other Sprint-4 controller tests broken.

---

### U3. OrderImportedV1 emission with per-line mapping resolution + event-type gating

**Goal:** Build the orchestrator layer that takes a parsed `WebhookEnvelope`, gates on `EventType == "order.created"`, calls `adapter.ParseOrderCreated` to get the `ExternalOrderDraft`, resolves each line's mapping through `IProductMappingService`, fails the whole import on any unmapped line (R6 corrected), and otherwise emits `OrderImportedV1` through the existing `IngestWebhookService.IngestAsync`.

**Requirements:** R4, R5, R6 (corrected).

**Dependencies:** U1 (adapter `ParseOrderCreated`), U2 (controller adapter route + parsed envelope already available).

**Files:**
- Modify: `src/Services/Channel/ShopFlow.Channel.Api/Controllers/WebhooksController.cs` — after the U2 parse step, gate on `EventType`, call new orchestration logic (either inline helper or a new service — implementer choice; if inline, keep the controller method under ~80 lines).
- Possibly create: `src/Services/Channel/ShopFlow.Channel.Application/Webhooks/OrderImportOrchestrator.cs` (or equivalent) if the controller logic exceeds readability — decision deferred to implementation.
- Modify: `src/Services/Channel/ShopFlow.Channel.Application/Webhooks/IngestWebhookService.cs` — likely no signature changes (already accepts `downstreamEventType` + `downstreamPayload` as parameters). Add the failed-import path: if upstream signals "import failed" (e.g., new flag or new `IngestAsync` overload), the service writes the `WebhookEvent` with `status=Failed` AND skips the outbox append. Alternative: keep `IngestWebhookService` unchanged and have the controller call a separate `IFailedWebhookRecorder.RecordFailedAsync` — implementer picks the cleaner shape.
- Modify: `src/Services/Channel/ShopFlow.Channel.Domain/Webhooks/WebhookEvent.cs` + `WebhookProcessingStatus.cs` — confirm `Failed` is a settable status and the persistence layer supports it (Sprint-4 likely ships this; verify and extend if not).
- Create: `tests/ShopFlow.Channel.IntegrationTests/OrderImportFlowTests.cs` for end-to-end emission tests.
- Possibly add: `tests/ShopFlow.Channel.UnitTests/Webhooks/OrderImportOrchestratorTests.cs` if a separate orchestrator service emerges.

**Approach:**
- After U2's `WebhookEnvelope`, the controller checks `envelope.EventType`:
  - `"order.created"` → call U1's `adapter.ParseOrderCreated(envelope)` → draft.
  - Otherwise → call `IngestWebhookService.IngestAsync` with a non-emitting event-type (or skip the outbox entirely — pick one shape). Sprint-4.5's policy: persist the `webhook_events` row with `status=Received`, do NOT append an outbox row. Downstream consumers for other event types arrive in Sprint-6+.
- For `order.created`: loop `draft.Lines` calling `IProductMappingService.ResolveAsync(channelId, line.ExternalSku, ct)`. Collect resolutions; if any returns null, the whole import fails:
  - Persist `WebhookEvent` with `status=Failed`.
  - Do NOT append outbox.
  - Log a structured warning with `(channel_id, unmapped_external_skus)` for operator-side investigation + manual `IProductMappingRepository.UpsertManualAsync` follow-up.
  - Controller returns HTTP 200 with `{ status: "import_failed", reason: "unmapped_skus", unmapped: [...] }` — 200 because the receiver did its job; the order is captured as Failed and replayable.
- For all-mapped: build `OrderImportedV1` with:
  - `OrderId = Guid.NewGuid()` (Channel-side minted, per contract doc).
  - `TenantId = requestContext.TenantId`.
  - `ChannelId` from path param.
  - `ChannelExternalOrderId = draft.ChannelExternalOrderId`.
  - `ShippingProfile = draft.ShippingProfile`.
  - `Lines = draft.Lines.Zip(resolutions, (l, r) => new OrderImportedLineV1(r.InternalSku, l.Qty))`.
  - `OccurredAt = envelope.OccurredAt`.
- Pass `typeof(OrderImportedV1).AssemblyQualifiedName!` as `downstreamEventType` and the `OrderImportedV1` instance as `downstreamPayload` to `IngestWebhookService.IngestAsync`.

**Execution note:** Test-first on the integration tests (AE3 + unmapped-line failure). Write the assertion that the outbox row's `event_type` column equals `typeof(OrderImportedV1).AssemblyQualifiedName` first; watch it fail; then swap in the production code.

**Patterns to follow:**
- `Sprint-4 U8 OrderImportedConsumer` shape (Outbound side) — implementer should sanity-check the round-trip shape against what the consumer expects.
- Serilog structured-warning pattern from Sprint-3-redux (`docs/solutions/2026-05-13-multi-row-cte-predicate-must-live-in-update.md` is unrelated content but uses the same logging conventions).

**Test scenarios** (`tests/ShopFlow.Channel.IntegrationTests/OrderImportFlowTests.cs`, tagged `Category=Integration`):
- **Happy path — Covers AE3.** Given a webhook payload with 2 lines: `("SP-001", 5)` exact-mapped to internal `"INV-001"`, and `("SP-002", 3)` mapped via Levenshtein to `"INV-002"`, when the controller receives a valid signed POST with `event_type = "order.created"`, then `channel_outbox_messages` contains exactly one row where `event_type == typeof(OrderImportedV1).AssemblyQualifiedName`, and the row's payload deserialises to an `OrderImportedV1` with `Lines == [("INV-001", 5), ("INV-002", 3)]`.
- **Unmapped line fails whole import — Covers R6 corrected.** Payload has lines `("SP-001", 5)` (mapped) and `("SP-XYZ", 3)` (no mapping, no Levenshtein match), when the controller processes it, the response is HTTP 200 with `{ status: "import_failed", reason: "unmapped_skus", unmapped: ["SP-XYZ"] }`, `webhook_events` row exists with `status=Failed`, and `channel_outbox_messages` is empty.
- **Replay of failed import is still idempotent.** Same payload from previous test POSTed a second time → second call detects the duplicate via UNIQUE-23505, returns `{ isDuplicate: true }`, no second `webhook_events` row, no outbox row. Confirms the Failed-status row STILL participates in idempotency (which the existing UNIQUE index guarantees).
- **Event-type gating — non-order event.** Payload with `event_type = "order.cancelled"` and valid signature → `webhook_events` row persists with `status=Received` (or equivalent), `channel_outbox_messages` empty, response 200 with `{ isDuplicate: false, status: "no_downstream" }` or equivalent.
- **OrderImportedV1 round-trip — consumer sees the real shape**: load the outbox row, deserialise as `OrderImportedV1` using `OutboxJsonOptions.Default`, assert all fields are populated and round-trip-stable (this is the regression catch against the consumer rejecting the producer's shape).
- **Multi-line payload preserves order**: 5-line payload, all mapped → emitted `OrderImportedV1.Lines` matches input line order (Zip preserves index).

**Verification:**
- All scenarios above pass against Testcontainers Postgres.
- The Outbound `OrderImportedConsumer` (Sprint-4 U8) accepts the new producer's payload (run an existing consumer-side test as regression to confirm no shape drift).
- `WebhooksController.cs` has no remaining placeholder strings (`grep -r "WebhookReceivedV1" src/` returns nothing).

---

### U4. TenantWebhookHarness — multi-tenant integration helper

**Goal:** Ship the reusable test fixture that provisions N tenants with Channels + signing secrets, exposes a typed `SendAsync(tenantSlug, channelId, eventType, dataObject)` that signs the payload with the right per-tenant secret and POSTs through the real `WebhooksController` pipeline.

**Requirements:** R7, R9.

**Dependencies:** U2 (controller's adapter route must exist for the harness to exercise it), U3 (the order-shape path is what the scale-gate tests measure end-to-end).

**Files:**
- Create: `tests/ShopFlow.Channel.IntegrationTests/Harness/TenantWebhookHarness.cs`.
- Create: `tests/ShopFlow.Channel.IntegrationTests/Harness/TenantWebhookHarnessFixture.cs` (xUnit collection fixture wrapping the Testcontainers Postgres + multi-tenant provisioning).
- Create: `tests/ShopFlow.Channel.IntegrationTests/Harness/SignedWebhookSender.cs` (HMAC-SHA256 helper using the per-tenant secret).
- Possibly modify: `tests/ShopFlow.Channel.IntegrationTests/ShopFlow.Channel.IntegrationTests.csproj` to ensure the `WebApplicationFactory` reference is in place (Sprint-4 may already have it).

**Approach:**
- Harness extends `PostgresFixture` pattern from `tests/ShopFlow.SharedKernel.IntegrationTests/PostgresFixture.cs` (single Testcontainers Postgres per collection).
- On `InitializeAsync`: spin Postgres, provision control-plane DB + N tenant DBs (default N=5), register each tenant's `channel_connections` row with a distinct secret, build a `WebApplicationFactory<Program>`-backed `HttpClient`.
- `SendAsync(tenantSlug, channelId, eventType, dataObject)`: serialise the payload, sign with the matching tenant's secret using the same `ShopeeSigner` the mock uses (mock-side and harness-side share the signing implementation to avoid drift), POST to `/api/channel/webhooks/shopee/{channelId}` with the `X-Shopee-Signature` header.
- Expose helpers for the most common assertion shapes: `CountOutboxRowsAsync(tenantSlug)`, `CountWebhookEventsAsync(tenantSlug)`, `AssertNoRowsInTenantAsync(tenantSlug)` etc. — keep the test bodies in U5 short.

**Execution note:** none (harness is infrastructure; no behavior assertions to write test-first).

**Patterns to follow:**
- `tests/ShopFlow.SharedKernel.IntegrationTests/PostgresFixture.cs` — Testcontainers lifecycle.
- `tools/mocks/shopee/ShopeeSigner.cs` — keep the harness-side signer in sync (or share the implementation).
- `tests/ShopFlow.SharedKernel.IntegrationTests/CrossTenantRoutingTests.cs` — multi-tenant test fixture precedent.

**Test scenarios** (smoke-only — the harness is exercised by U5):
- **Harness smoke**: 2 tenants provisioned, each can receive a single signed webhook end-to-end, each tenant's DB shows exactly 1 `webhook_events` row, NO cross-tenant DB contamination. (One `Category=Integration` `[Fact]` to ensure the harness itself is wired correctly; not tagged `Category=Load`.)

**Verification:**
- Harness smoke test passes against Testcontainers Postgres.
- `dotnet build` clean.
- The harness can be constructed and `SendAsync` invoked from a U5 test body without further setup.

---

### U5. Scale-gate test bodies

**Goal:** Flesh out the three currently-`Skip`'d `MultiTenantWebhookScaleGateTests`. Each test uses `TenantWebhookHarness` (U4) to drive load and assert per-tenant invariants. All three remain `Category=Load` tagged so per-PR CI skips them; nightly + on-demand CI runs them.

**Requirements:** R8, R9.

**Dependencies:** U4 (harness), U2 (controller wiring), U3 (order-emit pipeline — the burst test exercises the full path).

**Files:**
- Modify: `tests/ShopFlow.Channel.IntegrationTests/MultiTenantWebhookScaleGateTests.cs` — replace `[Fact(Skip = "...")]` with `[Fact]` on each method, remove `Skip` reasons, fill the bodies.

**Approach:**
- **Burst-200rps × 5 tenants × 5s**: 5 concurrent tenant tasks, each issuing 200 webhooks/s for 5 seconds (1,000 webhooks/tenant; 5,000 total). Measure per-tenant latencies (p50, p99). Compute fairness floor = min(per-tenant p99) / max(per-tenant p99). Assert per-tenant p99 < 200ms AND fairness floor ≥ 0.85.
- **Replay-100×**: provision 1 tenant + 1 channel, sign the same payload once, POST 100 times in parallel. Assert `channel_outbox_messages` for that tenant contains exactly 1 row.
- **Cross-tenant signature**: provision tenants A + B with distinct secrets. Sign a payload with A's secret, POST to B's channel URL. Assert response 401, zero rows in both tenants' `webhook_events`, zero rows in both tenants' `channel_outbox_messages`.

**Execution note:** Test-first cadence already in effect — the test files exist with `Skip`. Remove `Skip`, implement the body, expect it to pass once U2 + U3 + U4 land.

**Patterns to follow:**
- Sprint-3-redux U8 `MultiTenantOutboundScaleGateTests` — fairness calculation, warm-up phase, `NpgsqlConnection.ClearAllPools()` between tests.
- Sprint-1-redux U5 `MultiTenantScaleGateTests` — per-tenant assertion structure.

**Test scenarios** (`tests/ShopFlow.Channel.IntegrationTests/MultiTenantWebhookScaleGateTests.cs`, tagged `Category=Load`):
- **Burst-200rps × 5 tenants × 5s — Covers AE4 + R8.** Per-tenant p99 < 200ms, fairness floor ≥ 0.85.
- **Replay-100× same `(channel_id, provider_event_id)` — Covers R8.** Exactly 1 row in the target tenant's `channel_outbox_messages`.
- **Cross-tenant signature → 401 — Covers AE5 + R8.** 401 response, zero DB writes in either tenant.

**Verification:**
- All three tests pass when run with `dotnet test --filter "Category=Load"` against Testcontainers Postgres.
- No regressions in `Category=Integration` tests when the new bodies run alongside (use `ClearAllPools()` between to avoid connection-pool exhaustion).

---

### U6. Runtime smoke + sign-off + tag

**Goal:** Close Sprint-4.5. Where Docker is available locally, run a single end-to-end smoke through the Aspire orchestrator (mock posts a signed webhook → expect `OrderImportedV1` round-trip observable via the Outbound saga reaching a state past `New`). Where Docker is unavailable, document the deferral per Sprint-1/3/4 precedent. Write the sign-off doc, CHANGELOG entry, README + CLAUDE.md "Current stage" updates, plan frontmatter flip `status: active → completed`, annotated tag `v0.6.1-sprint-4.5`.

**Requirements:** R10, R11.

**Dependencies:** U1, U2, U3, U4, U5.

**Files:**
- Create: `docs/phase-gates/2026-05-DD-sprint-4.5-signoff.md` (date stamped at sign-off time, not plan-write time).
- Modify: `docs/CHANGELOG.md` — Sprint-4.5 entry.
- Modify: `README.md` — "Current stage" line.
- Modify: `CLAUDE.md` — "Current stage" section.
- Modify: `docs/plans/2026-05-14-001-feat-phase-2-sprint-4.5-webhook-followup-plan.md` (this file) — frontmatter `status: active → completed`, `completed: YYYY-MM-DD`, `signoff:` and `tag:` fields populated.

**Approach:**
- **Runtime smoke (conditional)**: if `docker ps` succeeds, `task up` brings the stack up, the harness or a manual `curl` to the Shopee mock's `POST /__send-webhook` drives one webhook, polling `saga_state` (or equivalent — pick based on Sprint-3-redux's saga harness shape) for the resulting saga to reach a state past `New`. If `docker ps` fails, document the same Sprint-4-style deferral in the sign-off.
- **Sign-off doc shape**: follows `docs/phase-gates/2026-05-13-sprint-4-signoff.md` (same headings, same measured-numbers + deferred-items table). Capture: build/test counts (expected ~272 unit, integration count grew, 3 Load tests now runnable), per-tenant p99 numbers from U5 (or "deferred — Docker unavailable on dev machine; first nightly CI run lands"), runtime smoke result.
- **Tag**: `git tag -a v0.6.1-sprint-4.5 -m "ShopFlow WMS v0.6.1 — Sprint-4.5 close (webhook receiver follow-up + scale-gate harness)"`. Annotated, message references the plan file path.
- **README + CLAUDE.md updates**: flip "Current stage" forward; CLAUDE.md `Next implementation step` points at Sprint-5 Stock Sync Engine plan (to be written).

**Execution note:** none (administrative close-out unit).

**Patterns to follow:**
- `docs/phase-gates/2026-05-13-sprint-4-signoff.md` — sign-off shape.
- `docs/CHANGELOG.md` — Sprint-4 entry style.
- Sprint-4 tag annotation message style.

**Test scenarios:** Test expectation: none — this is a docs + tag closure unit. The "verification" of correctness is that the prior 5 units' tests pass on the closing commit.

**Verification:**
- `dotnet build` 0/0 across all projects on the closing commit.
- `dotnet test --filter "Category!=Load"` all pass.
- `dotnet test --filter "Category=Load"` runs all 3 (or documented deferral exists).
- Sign-off doc complete; tag pushed; CHANGELOG + README + CLAUDE.md reflect the close.
- Plan frontmatter shows `status: completed`.

---

## Risks & Dependencies

| Risk | Likelihood | Impact | Mitigation |
|---|---|---|---|
| Adapter `ParseOrderCreated` reveals Shopee shape complexity the Sprint-4 fixtures don't cover (e.g., variant SKUs, multi-shop bundles) | Med | Low | U1 ships against the existing fixtures; new shapes surface as fixture additions in follow-up. The orchestrator's per-line loop handles N lines uniformly. |
| Per-line mapping resolution latency dominates the 200rps × 5 tenants scale gate, missing p99 < 200ms | Med | Med | Decision in Key Decisions: ship per-line, measure in U5, add `ResolveBatchAsync` as fast-follow if the budget is breached. The mapping service is in-process (no network); per-line cost should be sub-ms. |
| Outbound `OrderImportedConsumer` rejects the new producer's payload due to shape drift between U3's emission and Sprint-4 U8's deserialisation | Low | High | U3 test scenario explicitly round-trips a produced `OrderImportedV1` through deserialisation; mismatch surfaces at unit-test level not production. Sprint-4 U8 ships with consumer tests; re-run those after U3 lands. |
| The orchestrator's "fail whole import on unmapped" creates operator confusion (orders silently fail) | Low | Med | Mitigation: structured warning log with `(channel_id, unmapped_external_skus)` on every failed import. Phase-3 onboarding UI surfaces the `webhook_events.status = Failed` rows; operator manually upserts mappings via the Sprint-4 U6 `ProductMappingsController` and re-drives (replay via `POST /__send-webhook` or the existing UNIQUE catch). |
| U5's burst-200rps scale gate is too aggressive for the GitHub Actions runner; CI red on the first nightly | Med | Low | First-pass thresholds are tunable; if CI red surfaces, drop the burst rate (e.g., 100rps × 5 tenants × 5s) and document the dev-vs-CI delta in the Sprint-4.5 sign-off. Same posture Sprint-3-redux U8 took. |
| Cross-tenant signature test's assertion that "zero rows in tenant B's DB" is hard to verify cleanly if the controller writes the failed-signature row in tenant B's DB | Low | High | Existing Sprint-4 controller returns 401 BEFORE the tenant bind — no DB writes happen for an invalid signature. Verify via reading the existing controller code; if Sprint-4's order differs, the test catches it. |
| Test-first execution overhead on U2 + U3 inflates the sprint past 1 week | Low | Low | The execution-note discipline is lightweight (one failing integration test before each wire-up swap). The bigger risk is U5 if the GitHub Actions runner's Docker is slow; U5 can ship `Skip`'d again if CI surfaces unacceptable runtime, with a follow-up sub-sprint to land the bodies. |

---

## System-Wide Impact

- **`IChannelAdapter` gains one new method (`ParseOrderCreated`)** — all current implementations (just `ShopeeAdapter` in tree) must implement. Sprint-6 Lazada implements the same method against Lazada's order shape.
- **`WebhooksController` removes `ExtractProviderEventIdStub`** — no callers outside the controller; safe to delete.
- **`channel_outbox_messages.event_type` column** starts carrying `typeof(OrderImportedV1).AssemblyQualifiedName` strings — the existing `IOutboxRouteRegistry` (Sprint-4 U4) routes this as `SendKind.Send`; verified at Sprint-4 close.
- **`webhook_events.status = Failed`** becomes a real, written value (Sprint-4 ships the column + the Domain enum). Operator dashboards in Phase-3 will surface these; for Sprint-4.5 the structured log is sufficient.
- **No schema changes** — Sprint-4 ships the schema; Sprint-4.5 fills in the data side.
- **No downstream module changes** — Outbound `OrderImportedConsumer` shape unchanged; Inbound + Inventory unchanged.
- **CI/build**: `dotnet build` warn-as-error stays clean. Test count grows by ~10-15 across U1-U3 + 3 Load tests now runnable.

---

## Documentation / Operational Notes

- Sprint-4.5 sign-off doc lives at `docs/phase-gates/YYYY-MM-DD-sprint-4.5-signoff.md` (date at sign-off time).
- README "Current stage" flips to "Sprint-4.5 complete (YYYY-MM-DD)".
- CLAUDE.md "Current stage" updated to reflect Sprint-4.5 close + point at Sprint-5 plan as next.
- `docs/CHANGELOG.md` gets a Sprint-4.5 entry following the Sprint-4 shape.
- The `ExtractProviderEventIdStub` deletion is a "load-bearing carry-forward" — note in the sign-off so future PR reviewers understand the absence.
- If the orchestrator (U3) emerges as its own service (rather than inline in the controller), document its surface in a one-line `src/Services/Channel/AGENTS.md` delta.

---

## Sources & References

- Origin: `docs/brainstorms/2026-05-14-sprint-4.5-webhook-followup-requirements.md`
- Follows: `docs/phase-gates/2026-05-13-sprint-4-signoff.md` (Sprint-4 close + the deferred-item list this plan inherits)
- Canon (correctness anchor for R6): `src/Shared/ShopFlow.Contracts/Channel/OrderImportedV1.cs` doc comment — fail-whole-import on unmapped lines.
- Architectural canon: ADR-0003 (DB-per-tenant), `docs/redesign/02-technical-design-document.md` §5 (outbox + sync), §6 (webhooks + idempotency).
- Patterns to mirror: Sprint-3-redux U8 (multi-tenant scale-gate harness), Sprint-1-redux U5 (`MultiTenantScaleGateTests` shape), Sprint-4 U7 (Shopee mock + ShopeeSigner).
- Carried-forward learnings: `docs/solutions/2026-05-13-cross-module-outbox-table-name-collision.md` (per-module outbox table prefixing — `channel_outbox_messages` is the target).
- Tag history this sprint cuts from: `v0.6.0-sprint-4`.
- Tag this sprint produces: `v0.6.1-sprint-4.5`.
