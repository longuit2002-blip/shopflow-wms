---
title: "feat: Phase-3 Methodology Writeup — docs/methodology.md"
type: feat
status: active
date: 2026-05-18
origin: docs/brainstorms/2026-05-18-methodology-writeup-requirements.md
follows: docs/phase-gates/2026-05-17-sprint-5-signoff.md
tag_target: v0.8.0-methodology-writeup
---

# feat: Phase-3 Methodology Writeup — `docs/methodology.md`

## Overview

Ship single durable artifact `docs/methodology.md` (~7000-9000 từ) — comprehensive 7-sprint chronological case study + synthesis pattern catalog + friction section, documenting AI-assisted development methodology dùng trên ShopFlow WMS. Full honest về cả patterns đã work lẫn friction modes. Audience: future-self + dev khác clone repo (KHÔNG phải HR/recruiter).

Sprint output là 100% trong `docs/`; KHÔNG ship code WMS mới. Cadence theo Sprint-2/3/4/5 sign-off style: per-unit verification + final wrap-up unit (README/CLAUDE/CHANGELOG/sign-off/tag). Tag target: `v0.8.0-methodology-writeup`. Cut branch mới từ `v0.7.0-sprint-5`.

---

## Problem Frame

7 sprints worth of methodology evidence đã rải khắp repo (6 brainstorm, 8 plan, 8 sign-off, 3 ADR, 5 solutions, 9 git tags) nhưng chưa được synthesize thành coherent artifact đọc được. Without writeup:

- **Future-self 6 tháng sau** phải re-derive methodology từ scattered evidence
- **Dev khác clone repo** phải đọc rất nhiều file để hiểu pattern repeatability
- **"Chứng minh dùng được AI hiệu quả"** thiếu một point-of-reference để link tới
- **Friction modes** (context window pressure, Skip'd deferrals, KTD7 emerging mid-sprint, .NET 9 SDK gap on dev machine) không có nơi rút bài học cho project sau

Project's primary deliverable đã shift: WMS code không còn là main artifact — methodology proof IS. Sprint kế tiếp ship cái artifact đó.

---

## Requirements Trace

Origin requirements ([docs/brainstorms/2026-05-18-methodology-writeup-requirements.md](../brainstorms/2026-05-18-methodology-writeup-requirements.md)) → U-IDs:

| R-ID | Requirement | Owning U-IDs |
|---|---|---|
| R1 | Ship single file `docs/methodology.md` (top-level, không split) | U1, U7 |
| R2 | Length ~7000-9000 từ tổng (sprint 500-800 mỗi, synthesis 2000-3000, friction 1000) | U1-U7 |
| R3 | Structure: lead-in → 7 chronological sprint sections → synthesis → friction → forward-looking | U1, U2, U3, U4, U5, U6, U7 |
| R4 | Each sprint section: what built (1-2 sentences) + KTDs (planned + emergent) + deferrals + worked/friction. File paths + commit hashes + sign-off links | U2, U3, U4, U5 |
| R5 | Synthesis pattern catalog: cadence, KTD discovery, subagent dispatch, deferral, context management | U6 |
| R6 | Friction section ~1000 từ: context window pressure, subagent re-dispatch on limit, .NET 9 SDK gap, Skip'd deferral honesty, KTD7 mid-sprint, CRLF noise | U7 |
| R7 | Heavy linking: repo-relative file paths, short commit hashes, ADR links, sign-off doc links, solutions/ links | U1-U7 |
| R8 | Code snippets sparing (~15 line max); prefer file:line range references | U2-U6 |
| R9 | Tone: credible case study, không marketing. Acknowledge costs với benefits | U1-U7 |
| R10 | Anti-patterns / kicking-the-can openly discussed (e.g., Sprint-5 U9 Skip'd slots) | U5, U7 |
| R11 | Acknowledge: one project, one solo dev, one stack — không claim universal applicability | U1, U6 |
| R12 | NO new code in `src/`. Output entirely in `docs/`. Tag `v0.8.0-methodology-writeup` | U7 |

Acceptance Examples AE1-AE5 carry forward as verification criteria trong U1 (structure), U5 (friction modes), U7 (link integrity + scope).

---

## Scope Boundaries

### In scope

- All R1-R12 (full honest 7-sprint chronological + synthesis + friction).
- Branch `feat/phase-3-methodology-writeup` cut từ `v0.7.0-sprint-5`.
- Sprint sign-off doc + CHANGELOG entry + README + CLAUDE.md "Current stage" update.
- Annotated tag `v0.8.0-methodology-writeup`.

### Deferred to Follow-Up Work

- **Public blog derivative** (~3000-4000 từ adapted for cold reader, target dev.to / personal blog). Separate brainstorm/plan/work cycle riêng AFTER internal doc settles. Có thể là `feat/phase-3-blog-derivative` cut từ `v0.8.0-methodology-writeup`.
- **Process improvements to AGENTS.md / skill cadence** dựa trên findings từ writing the doc. Reflection cycle riêng — current sprint chỉ document hiện trạng, không sửa methodology.
- **Reusable template repo extraction** — chỉ nếu external blog feedback cho thấy đủ demand. Not committed.

### Out of scope (rejected)

- **WMS feature work** — Sprint-5.5 scale gate, Sprint-6 Analytics, deployment doc, live demo URL, Gateway hardening, observability dashboards.
- **HR/recruiter-targeting README hero pitch** — current README giữ nguyên (technical, dense).
- **Code snippets > 15 lines** — prefer file:line references.
- **Quantitative measurements** beyond existing sign-off numbers — không thêm benchmark project.
- **Comparative analysis vs Cursor / Aider / Codex** — doc speaks only to ShopFlow WMS evidence.

---

## Key Technical Decisions

### KTD1 — Single-file `docs/methodology.md` ở top-level, không multi-doc split

**Decision.** File path `docs/methodology.md`. Không tạo `docs/methodology/` subfolder. Hand-rolled TOC anchors ở đầu doc.

**Rationale.** 1 file dễ link (single URL), dễ search trong markdown viewer, future-self search-friendly. Multi-doc split (`cadence.md` / `deferral.md` / `friction.md`) defer nếu doc bloat quá khả năng đọc 1 lần. ~7000-9000 từ vẫn trong khả năng đọc 30-45 phút end-to-end.

### KTD2 — Chronological narrative trước, synthesis pattern catalog sau

**Decision.** Structure: lead-in → 7 sprint sections chronologically → synthesis pattern catalog → friction section → outro. KHÔNG pattern-catalog-first.

**Rationale.** Comprehensive coverage (đã chốt brainstorm) cần story arc. Pattern catalog alone mất chronology + causality — ví dụ: KTD7 emergence (Sprint-5 U7) chỉ make sense nếu Sprint-3-redux K12 pattern đã được established earlier. Story-first đảm bảo reader có context khi đến synthesis section.

### KTD3 — Reference density: heavy linking, sparing code snippets

**Decision.** Repo-relative file paths throughout (e.g., `src/Services/Channel/ShopFlow.Channel.Infrastructure/Adapters/ShopeeAdapter.cs`). Short commit hashes (7 chars). ADR links + sign-off links + solutions/ links. Code snippets ≤ 15 lines; if longer, file:line reference (e.g., "see `src/.../ReservationRepository.cs:285-320`").

**Rationale.** Reader có thể click qua đọc actual source khi cần. Sparing code keeps doc readable as prose; heavy embedded code turns doc into a code dump.

### KTD4 — Full-honest friction section, không success-only

**Decision.** 6+ named friction modes với concrete examples. NO marketing phrases ("AI made this 10x faster"). Acknowledge Sprint-5 U9 kicking-the-can openly; document Sprint-5.5 follow-up as known cost.

**Rationale.** Self-validation goal đòi credibility. Success-only doc reads như marketing và mất audience trust. Reader sau cần biết trade-offs trước khi áp dụng method cho project của họ.

### KTD5 — Mermaid diagrams sparing (1-2 max), không boilerplate

**Decision.** 1-2 Mermaid diagrams maximum trong synthesis section: cadence flow (brainstorm → plan → work → sign-off) + KTD discovery cycle (plan-time vs mid-sprint emergence). KHÔNG sprint flowcharts (chronological prose đủ).

**Rationale.** Diagrams kéo focus khỏi narrative; chỉ dùng khi pattern visual rõ hơn prose. KTD discovery cycle có 2 entry points (plan-time + mid-sprint) — diagram làm rõ. Cadence diagram giúp reader skim section.

### KTD6 — Verification per unit là word-count range + link integrity, không có test code

**Decision.** Mỗi unit có Verification field với specific outcome targets: word count range, link integrity (no 404), content checklist (e.g., "all 5 KTDs mentioned"). KHÔNG có `*.cs` test file.

**Rationale.** Sprint deliverable là doc, không phải code. Test scenarios chuyển sang Verification criteria. AE1-AE5 từ brainstorm translate trực tiếp thành Verification.

---

## Output Structure

```
docs/
└── methodology.md  (new — single file ~7000-9000 từ)
```

Plus modifications:
- `README.md` (current-stage block updated)
- `CLAUDE.md` (current-stage block updated)
- `docs/CHANGELOG.md` (new entry for 2026-05-XX methodology writeup)
- `docs/phase-gates/2026-05-XX-methodology-writeup-signoff.md` (new sign-off doc)
- `docs/plans/2026-05-18-001-feat-methodology-writeup-plan.md` (this file; frontmatter `status: active → completed` at close)

No `src/` modifications.

---

## High-Level Document Structure

*This illustrates intended doc shape; implementing agent treats as directional guidance, not literal copy-paste template.*

```
# ShopFlow WMS — AI-Assisted Development Methodology

[Hand-rolled TOC with anchors]

## Context — What this doc is, what it's not
  (~300 từ; framing: one project, one solo dev, one stack; not universal claim)

## How the project was built — chronological sprint narrative

### Phase-0-redux — Foundation (DB-per-tenant pivot)
### Sprint-1-redux — Reservation ledger
### Sprint-2-redux — Inbound module
### Sprint-2.5 — Cross-module outbox prefix
### Sprint-3-redux — Outbound saga
### Sprint-4 — Channel webhook ingress
### Sprint-4.5 — Webhook follow-up + scale gate
### Sprint-5 — Stock sync engine (egress)

  (Each section: 500-800 từ; what was built / KTDs / deferrals / what worked / friction)

## Synthesis — Patterns that compounded across sprints

### Cadence: brainstorm → plan → work → sign-off
### KTD discovery — plan-time vs mid-sprint emergence
### Subagent dispatch — context isolation under pressure
### Deferral pattern — Sprint-4 → 4.5, Sprint-5 → 5.5
### Context management — AGENTS.md / CLAUDE.md / session-resume hooks

  (Total ~2500 từ; includes 1-2 Mermaid diagrams; cross-sprint examples)

## Friction — What didn't work, what cost more than expected

  (~1000 từ; 6+ named modes, chronological)
  - Context window pressure mid-sprint
  - Subagent re-dispatch when usage limits hit mid-task
  - .NET 9 SDK gap on dev machine (CI validates; can't local build)
  - Skip'd deferral pattern — kicking the can?
  - KTD7 emerged mid-sprint (Sprint-5) — surface sooner?
  - CRLF/LF line-ending noise per commit

## Forward-looking — Open questions, what would be different next time

  (~500 từ; what this project surfaced as worth changing for project sau)

## Appendix — References

  (file paths grouped by category: brainstorms, plans, sign-offs, ADRs, solutions, tags)
```

---

## Implementation Units

### U1. Doc skeleton + lead-in + TOC + reference inventory

**Goal:** Tạo `docs/methodology.md` với full structure (headers + anchors) + lead-in section (~300 từ) + TOC + appendix reference inventory (file paths cho tất cả brainstorm/plan/sign-off/ADR/solutions). Skeleton phải đủ để U2-U6 fill từng section riêng biệt.

**Requirements:** R1, R3, R7, R9, R11.

**Dependencies:** none.

**Files:**
- `docs/methodology.md` (new — skeleton + lead-in + TOC + appendix)
- `docs/plans/2026-05-18-001-feat-methodology-writeup-plan.md` (this file; reference)

**Approach:**
- Top-level heading: `# ShopFlow WMS — AI-Assisted Development Methodology`
- Hand-rolled TOC ở đầu doc với markdown anchors (e.g., `[Cadence](#cadence-brainstorm--plan--work--sign-off)`).
- Lead-in section "Context — What this doc is, what it's not":
  - 1 paragraph project framing (12-week portfolio WMS, solo dev, .NET 9 + Postgres modular monolith)
  - 1 paragraph methodology framing (Claude Code + compound-engineering skill cadence)
  - 1 paragraph honest acknowledgment: one project, one solo dev, one stack — không universal claim
  - 1 paragraph reader expectation: "if you came expecting 'AI saved X% time', this is not that doc"
- Section headers (placeholder only — chỉ heading, không nội dung) cho:
  - 7 sprint subsections (Phase-0-redux → Sprint-5)
  - Synthesis subsections (5 pattern categories)
  - Friction subsection
  - Forward-looking subsection
- Appendix reference inventory ở cuối — grouped lists:
  - **Brainstorms**: 5 files (link mỗi cái với 1-line description)
  - **Plans**: 8 files (link + description)
  - **Sign-offs**: 8 files
  - **ADRs**: 3 files
  - **Solutions**: 5 files
  - **Git tags**: 9 tags với date + scope description

**Patterns to follow:**
- `docs/phase-gates/2026-05-17-sprint-5-signoff.md` — heading + frontmatter style.
- `docs/redesign/01-product-development-plan.md` — long-doc structure with TOC.

**Verification:**
- File exists at `docs/methodology.md`
- Word count: 800-1000 từ (lead-in 300 + TOC + appendix ~500)
- All 18+ section headers present (1 lead-in + 7 sprints + 5 synthesis + 1 friction + 1 forward + 1 appendix)
- Appendix lists all 5+8+8+3+5 = 29 doc links + 9 git tags
- TOC anchors resolve (manual check: click each anchor in markdown preview)

---

### U2. Phase-0-redux + Sprint-1-redux sections

**Goal:** Viết 2 sprint sections: Phase-0-redux (DB-per-tenant pivot, foundation) + Sprint-1-redux (reservation ledger).

**Requirements:** R3, R4, R7, R8, R9.

**Dependencies:** U1.

**Files:**
- `docs/methodology.md` (modify — fill Phase-0-redux + Sprint-1-redux sections)

**Approach:**
Each section ~500-800 từ với consistent shape:

**Phase-0-redux section structure**:
- **What was built** (1-2 câu): foundation pivot từ RLS-shared → DB-per-tenant. ControlPlane catalog DB + per-module Aspire bootstrap + shopflow-migrate CLI.
- **KTDs** (planned + emergent): ADR-0003 pivot itself; D1-D4 from CLAUDE.md (PgBouncer pool sizing, catalog cache TTL, migration smoke test assertions, routing middleware priority).
- **Deferrals**: Aspire cold-start measurement deferred to U10 sign-off; CSharpier formatting cleanup deferred.
- **What worked**: 10-unit cadence; analyzer-as-error promotion in U10; smoke test for `[Migration]` attributes.
- **Friction**: ShopFlow0001-0004 analyzers + 23 files drift; sign-off cleanup commit needed.
- **Reference links**: [docs/plans/2026-05-11-002-phase-0-redux-bootstrap-plan.md](../plans/2026-05-11-002-phase-0-redux-bootstrap-plan.md), [docs/phase-gates/2026-05-12-phase-0-redux-signoff.md](../phase-gates/2026-05-12-phase-0-redux-signoff.md), [ADR-0003](../adr/0003-database-per-tenant-for-compliance.md), tag `v0.2.0-phase-0-redux`.

**Sprint-1-redux section structure**:
- **What was built** (1-2 câu): Reservation ledger với conditional-CTE INSERT at READ COMMITTED (v3.0 correction từ v2.0 SERIALIZABLE).
- **KTDs**: ReadCommitted vs SERIALIZABLE correction (origin learning); FsCheck Replay gamma format; Property 5 read-back surface gap.
- **Deferrals**: Scale gate wall-time measurement deferred (Docker daemon dev machine gap); FsCheck Replay format documented as learning.
- **What worked**: U4 property tests — "zero test-body edits when port pivots" — actually relaxed to "re-derive properties against new port shape"; concurrent-oversell property catches race.
- **Friction**: Property tests pivot when port signature changed mid-sprint; Property 5 raw-SQL read-back stop-gap.
- **Reference links**: [docs/plans/2026-05-11-003-phase-1-sprint-1-redux-reservation-ledger-plan.md](../plans/2026-05-11-003-phase-1-sprint-1-redux-reservation-ledger-plan.md), [docs/phase-gates/2026-05-12-sprint-1-redux-signoff.md](../phase-gates/2026-05-12-sprint-1-redux-signoff.md), [docs/solutions/2026-05-12-readcommitted-conditional-cte-correctness.md](../solutions/2026-05-12-readcommitted-conditional-cte-correctness.md), tag `v0.3.0-sprint-1-redux`.

**Tone reminders** (R9, R11):
- Sprint-1-redux Property 5 stop-gap = honest friction, không "we cleverly used raw SQL".
- Phase-0-redux 23-file CSharpier drift = friction documented openly.

**Patterns to follow:**
- Existing sign-off doc structure for "what shipped" + "deviations" sections.

**Verification:**
- 2 sections written; section length each 500-800 từ (total ~1300 từ).
- Each section has all 6 sub-bullets: built / KTDs / deferrals / worked / friction / refs.
- All file path links resolve (manual click-check in markdown preview).
- ADR-0003 link present in Phase-0-redux section.
- 1 solutions/ link in Sprint-1-redux section.
- 2 tags referenced (`v0.2.0-phase-0-redux`, `v0.3.0-sprint-1-redux`).
- Friction explicitly named in each (no whitewashing).

---

### U3. Sprint-2-redux + Sprint-2.5 + Sprint-3-redux sections

**Goal:** Viết 3 sprint sections covering Inbound module → cross-module outbox prefix fix → Outbound fulfillment saga.

**Requirements:** R3, R4, R7, R8, R9.

**Dependencies:** U1.

**Files:**
- `docs/methodology.md` (modify — fill Sprint-2-redux + Sprint-2.5 + Sprint-3-redux sections)

**Approach:**
Each section 500-700 từ với consistent shape (same template as U2).

**Sprint-2-redux highlights**:
- Built: Inbound module + Inventory bin/zone schema + MassTransit RabbitMQ flip (W6 → W4)
- KTDs: Domain event path swapped for explicit `IInboundOutbox`; MediatR wrapper deferred; identity-column annotation fix (Npgsql typed enum)
- Deferrals: Single-tenant-DB cross-module flow test deferred → Sprint-2.5 candidate (architecture finding)
- Friction: Cross-module outbox table-name collision surfaced as architecture finding; documented in solutions/
- Refs: [docs/plans/2026-05-13-001-feat-phase-1-sprint-2-redux-inbound-plan.md](../plans/2026-05-13-001-feat-phase-1-sprint-2-redux-inbound-plan.md), [docs/phase-gates/2026-05-13-sprint-2-redux-signoff.md](../phase-gates/2026-05-13-sprint-2-redux-signoff.md), [docs/solutions/2026-05-13-cross-module-outbox-table-name-collision.md](../solutions/2026-05-13-cross-module-outbox-table-name-collision.md), tag `v0.4.0-sprint-2-redux`

**Sprint-2.5 highlights** (point release, ~3 units):
- Built: Per-module outbox table-name prefix (`inbound_outbox_messages` / `inventory_outbox_messages`); cross-module flow tests
- KTDs: `OutboxJsonOptions.Default` in SharedKernel — surfaced latent JSON case-sensitivity bug
- Friction: Showed value of small point-release sprints (Sprint-2.5 pattern) — proof-of-concept for Sprint-4.5 / Sprint-5.5 cadence
- Refs: [docs/phase-gates/2026-05-13-sprint-2.5-signoff.md](../phase-gates/2026-05-13-sprint-2.5-signoff.md), tag `v0.4.1-sprint-2.5`

**Sprint-3-redux highlights**:
- Built: Outbound module + MassTransit saga (11 states) + EF saga repository với K12 per-tenant DbContext binding + 9 cross-module contracts + `IPickQueue` + mocked shipping carrier + scale gate
- KTDs: K11 multi-row CTE concurrency fix (predicate must live inside UPDATE); K12 per-tenant DbContext binding; K15 MT.EFCore 8.3.4 + EF Core 9 binding verified; K13 envelope-type → endpoint routing deferred (Phase-2 prereq for W6 split)
- Deferrals: U8 scale-gate harness body "saga path bypassed — operator-pipeline measurement only"; documented honestly as "real-saga-throughput-under-load is a Phase-2 production-CI measurement gap"
- Friction: K11 caught by Sprint-1-redux's concurrent-oversell test — story of test-first cadence catching pre-check CTE race; MT 8.x publish DSL trap (`PublishAsync(ctx.Init<T>(...))` silently fails)
- Refs: [docs/plans/2026-05-13-002-feat-phase-1-sprint-3-redux-outbound-plan.md](../plans/2026-05-13-002-feat-phase-1-sprint-3-redux-outbound-plan.md), [docs/phase-gates/2026-05-13-sprint-3-redux-signoff.md](../phase-gates/2026-05-13-sprint-3-redux-signoff.md), [docs/solutions/2026-05-13-multi-row-cte-predicate-must-live-in-update.md](../solutions/2026-05-13-multi-row-cte-predicate-must-live-in-update.md), tag `v0.5.0-sprint-3-redux`

**Tone reminders**: U8 saga-bypass = honest friction documented, không spun as "we measured a different surface intentionally".

**Patterns to follow:** Same template as U2.

**Verification:**
- 3 sections written; total ~1800-2100 từ.
- Each section has all 6 sub-bullets.
- All 8+ doc links resolve.
- 2 solutions/ links (outbox collision + multi-row CTE).
- 3 tags referenced.
- Sprint-2.5 section explicitly frames itself as "proof-of-concept for follow-up cadence" (sets up Sprint-4.5 / 5.5 pattern in U6 synthesis).

---

### U4. Sprint-4 + Sprint-4.5 sections

**Goal:** Viết 2 sprint sections covering Channel webhook ingress (Sprint-4) + webhook follow-up closure (Sprint-4.5).

**Requirements:** R3, R4, R7, R8, R9, R10.

**Dependencies:** U1.

**Files:**
- `docs/methodology.md` (modify — fill Sprint-4 + Sprint-4.5 sections)

**Approach:**
Each section ~500-800 từ. **Anti-pattern emphasis (R10)**: Sprint-4 → 4.5 là canonical example của deferral pattern; emphasize honest discussion.

**Sprint-4 highlights**:
- Built: Channel module (3 aggregates + webhook receiver + adapter framework + Shopee mock + ProductMapping engine + OrderImportedV1 + K13 OutboxRoute registry close)
- KTDs: K13 envelope-type → endpoint routing closed; Sprint-2.5 per-module outbox prefix carry-forward; UNIQUE-23505 idempotency anchor
- Deferrals (4 items): U5 scale-gate harness body deferred (3 `Skip`'d slots); `provider_event_id` stub (body+sig hash); `OrderImportedV1` not yet emitted; runtime smoke deferred (Docker daemon)
- Friction: 4 deferrals shipped together → high deferral count; setup Sprint-4.5 closure cadence
- Refs: [docs/plans/2026-05-13-003-feat-phase-2-sprint-4-channel-webhook-plan.md](../plans/2026-05-13-003-feat-phase-2-sprint-4-channel-webhook-plan.md), [docs/phase-gates/2026-05-13-sprint-4-signoff.md](../phase-gates/2026-05-13-sprint-4-signoff.md), tag `v0.6.0-sprint-4`

**Sprint-4.5 highlights**:
- Built: 4 Sprint-4 deferrals closed as ~1-week point release; ChannelAdapter.ParseOrderCreated + WebhookOrchestrator (event-type gating + per-line SKU resolve) + R6 reversal (fail-whole-import canon) + TenantWebhookHarness + 3 scale-gate bodies + sentinel skip-event for non-`order.created`
- KTDs: R6 reversal (canon-correct emit-fail-whole-import vs brainstorm `InternalSku=null`); WebhookOrchestrator new Application service; harness `eventId` knob for replay tests
- Deferrals: Runtime smoke còn deferred (Docker daemon); per-event-type policy beyond `order.created` (Sprint-6+); mapping batch resolution
- Friction: U1 field-name correction (idealized brainstorm vs real Shopee fixture); R6 reversal mid-plan when reading actual contract docs
- Refs: [docs/plans/2026-05-14-001-feat-phase-2-sprint-4.5-webhook-followup-plan.md](../plans/2026-05-14-001-feat-phase-2-sprint-4.5-webhook-followup-plan.md), [docs/phase-gates/2026-05-15-sprint-4.5-signoff.md](../phase-gates/2026-05-15-sprint-4.5-signoff.md), tag `v0.6.1-sprint-4.5`

**Critical framing (R10)**: Sprint-4 deferred 4 items + Sprint-4.5 closed them = honest example of deferral pattern. Discuss openly:
- Why defer rather than push Sprint-4 longer? (scope discipline; sprint cadence integrity)
- Cost of deferral? (1-week follow-up sprint = real time investment)
- Why pattern is reusable? (Sprint-5 → 5.5 was already predicted at Sprint-4.5 close)

**Patterns to follow:** Sprint-4.5 sign-off doc — deviations section is template for honest deferral discussion.

**Verification:**
- 2 sections written; total ~1300-1600 từ.
- Each section has all 6 sub-bullets.
- Sprint-4 section explicitly names 4 deferrals (count + content).
- Sprint-4.5 section explicitly frames as "closure sprint for 4 deferrals" + names which closed.
- Sprint-4 → 4.5 transition explicitly discussed (sets up synthesis pattern in U6).
- 2 tags referenced.

---

### U5. Sprint-5 section (longest, most detailed)

**Goal:** Viết Sprint-5 section — most recent + richest evidence base (10 units, 2 KTDs incl. mid-sprint emergent KTD7, 2 Skip'd scale-gate slots).

**Requirements:** R3, R4, R7, R8, R9, R10.

**Dependencies:** U1.

**Files:**
- `docs/methodology.md` (modify — fill Sprint-5 section)

**Approach:**
Section ~1000-1200 từ (longer than other sprints — most evidence, most emergent KTDs, latest example).

**Sprint-5 highlights**:
- Built: ShopFlow.StockSync module (7th logical module) with 4-layer isolation pipeline (coalescing buffer → priority queue → token bucket → circuit breaker) + StockLevelChangedV1 canonical event + ShopeeAdapter.PushStockUpdate body + SkuFlag admin + caching wrapper + 10 implementation units shipped serially
- KTDs:
  - **KTD1** (plan-time): replaces literal R1 (3 transition events) with single canonical `StockLevelChangedV1` — discovered by reading actual contract definitions during plan-time. Avoids StockSync ↔ Outbound coupling. Documents "reading-actual-code-during-planning" pattern.
  - **KTD2-6** (plan-time): flag table location, library choice, coalescing impl, persistence boundary, module identity
  - **KTD7** (mid-sprint, U7): `ISkuFlagRepository` port signature change to take `Guid tenantId` explicitly. Surfaced when singleton wrapper scope-binding analysis revealed per-tenant cache key needed explicit tenant. Updates consumer + 4 NSubstitute call sites in U3 unit tests.
- Deferrals (R10 explicit): 2 `Category=Load` scale-gate slots ship Skip'd per Sprint-4 U9 precedent. Sprint-5.5 follow-up sprint required. Honest "kicking the can?" discussion — same precedent đã proven Sprint-4 → 4.5.
- Subagent dispatch (R5 setup): Used serial subagents for U3-U9 (7 of 10 units) to manage context window pressure. Each subagent had fresh context + full plan unit metadata. Trade-off: subagent re-investigation overhead vs parent context preservation.
- Friction (R10): Subagent re-dispatch when usage limit hit mid-U5; .NET 9 SDK gap (dev machine has 8.0.407, global.json pins 9.0.305); KTD7 mid-sprint emergence — analysis: could it have surfaced sooner if U3 plan-time had explicit tenant-scope question?
- Refs: [docs/plans/2026-05-16-001-feat-phase-2-sprint-5-stock-sync-plan.md](../plans/2026-05-16-001-feat-phase-2-sprint-5-stock-sync-plan.md), [docs/phase-gates/2026-05-17-sprint-5-signoff.md](../phase-gates/2026-05-17-sprint-5-signoff.md), [docs/brainstorms/2026-05-16-sprint-5-stock-sync-requirements.md](../brainstorms/2026-05-16-sprint-5-stock-sync-requirements.md), [docs/brainstorms/2026-05-16-sprint-5-visual.html](../brainstorms/2026-05-16-sprint-5-visual.html) (visual companion when prose dialogue got confused), tag `v0.7.0-sprint-5`

**Section is anchor for U6 synthesis**:
- KTD1 plan-time correction → cited in synthesis "KTD discovery" subsection
- KTD7 mid-sprint emergence → cited in synthesis "KTD discovery" subsection
- Serial subagents → cited in synthesis "Subagent dispatch" subsection
- Visual HTML companion → cited in synthesis "Context management" subsection (when prose dialogue got confused, switched to visual)
- 2 Skip'd slots → cited in synthesis "Deferral pattern" subsection

**Patterns to follow:** Sprint-5 sign-off doc — deviations section + KTD recap.

**Verification:**
- Section written; length 1000-1200 từ.
- All 7 KTDs (1-6 + 7) explicitly named.
- 2 Skip'd slots explicitly discussed with rationale + cost.
- Subagent dispatch pattern surfaced (sets up U6 synthesis link).
- Visual companion HTML referenced (sets up U6 context-management synthesis link).
- 4+ doc links resolve (plan + sign-off + brainstorm + visual).
- `v0.7.0-sprint-5` tag referenced.

---

### U6. Synthesis section — pattern catalog

**Goal:** Write synthesis section extracting reusable patterns across all 7 sprints — chronology dissolves into pattern categories.

**Requirements:** R3, R5, R7, R8, R9, R11.

**Dependencies:** U2, U3, U4, U5 (synthesis reuses sprint-section evidence; should be written after sprint sections settle).

**Files:**
- `docs/methodology.md` (modify — fill Synthesis section)

**Approach:**
Section ~2500 từ. 5 pattern subsections.

**Subsection 1 — Cadence: brainstorm → plan → work → sign-off** (~500 từ)
- Pattern: each sprint = (brainstorm doc, plan doc, work commits, sign-off doc). 7 instances proves reusability.
- Why it works: brainstorm answers WHAT (product decisions); plan answers HOW (technical decisions); work executes; sign-off captures deviations + KTDs for future-self.
- Specific evidence: Sprint-2-redux → 2.5 → 3-redux follows cycle 3x in 1 day (May 13) — cadence is fast when patterns settled.
- Mermaid diagram: brainstorm → plan → work → sign-off → tag → next-brainstorm loop
- Honest note: cadence overhead is non-trivial (~10-15% sprint time on docs); pay-off is future-self speed.

**Subsection 2 — KTD discovery: plan-time vs mid-sprint emergence** (~500 từ)
- Pattern: KTDs surface in 2 modes — plan-time (reading actual code during planning catches false assumptions) + mid-sprint (implementation surfaces issue brainstorm/plan missed).
- Plan-time examples: KTD1 Sprint-5 (3-event → 1-event consume); R6 reversal Sprint-4.5 (contract canon vs brainstorm idealization); K11 Sprint-3-redux pseudo-code → real CTE.
- Mid-sprint examples: KTD7 Sprint-5 U7 (`ISkuFlagRepository` tenantId); MT 8.x publish DSL trap Sprint-3-redux U4.
- Lesson: "reading actual code during plan-write" pattern catches more than brainstorm interview alone.
- Mermaid: KTD discovery cycle with 2 entry points + feedback into sign-off
- Honest: KTD7 case study — could it have surfaced earlier? Probably yes, with an explicit tenant-scope checkbox in U3 plan-time.

**Subsection 3 — Subagent dispatch: context isolation under pressure** (~500 từ)
- Pattern: Serial subagents per unit. Each subagent gets fresh context + full plan unit metadata.
- Why: 10-unit sprint exceeds single-context attention budget; subagent pattern preserves orchestrator quality.
- Sprint-5 case study: U3-U9 dispatched serially. Parent reviewed + committed; subagent wrote code. Cost: ~30% re-investigation overhead per subagent.
- Honest note: subagent dispatch is partial fix. Doesn't solve usage-limit re-dispatch (subagent has to re-load context from scratch).

**Subsection 4 — Deferral pattern: Sprint-4 → 4.5, Sprint-5 → 5.5** (~500 từ)
- Pattern: ship harness shell với `[Fact(Skip = "...")]` markers + sign-off documents deferral + follow-up sub-sprint closes.
- Why: keeps sprint cadence integrity; doesn't pretend scope was met when it wasn't.
- 2 instances prove reusability: Sprint-4 deferred 4 items → Sprint-4.5 closed 4; Sprint-5 deferred 2 scale-gate slots → Sprint-5.5 (not yet built but pattern says: 1-week follow-up).
- Honest "kicking the can?" discussion: each deferral case-by-case — Sprint-4 U9 (legit, harness gap), Sprint-5 U9 (legit, same gap). vs unhealthy pattern: deferring without sign-off doc honesty.
- Cost: real follow-up sprint time; production primitives proven by unit/integration tests in between.

**Subsection 5 — Context management: AGENTS.md / CLAUDE.md / session-resume hooks** (~500 từ)
- Pattern: AGENTS.md = persistent agent config (rules, conventions); CLAUDE.md = current-stage block + sprint history; session-resume hook = continuity across context window resets.
- Why: project state too large for in-context loading; persistent docs let any session resume.
- Visual companion HTML when prose gets dense (Sprint-5 case study — user said "tôi đang bị nhiễu confuse rồi"); shipped `docs/brainstorms/2026-05-16-sprint-5-visual.html` to unstuck conversation.
- Honest: AGENTS.md/CLAUDE.md grow over time → re-reading cost at session start increases. Pruning needed eventually.

**Closing framing (R11)**: 1 paragraph "these patterns came from one project, one solo dev, one stack — may not generalize. They work for the specific shape: solo work + long-running project + persistent docs + Claude Code + ce skills."

**Patterns to follow:**
- Existing CLAUDE.md "Bootstrap stance" section — narrative-with-references style.
- Sprint-5 sign-off doc — "Key technical decisions (recap of plan KTDs)" structure.

**Verification:**
- Section written; total length ~2500 từ; 5 subsections each 400-600 từ.
- All 5 pattern subsections present with title + description + cross-sprint evidence + honest note.
- 2 Mermaid diagrams present (cadence loop + KTD discovery cycle).
- Each subsection references 2+ sprints' worth of evidence.
- Closing R11 framing paragraph present (no universal-applicability claim).

---

### U7. Friction section + forward-looking + outro + sprint wrap-up

**Goal:** Write friction section (~1000 từ, 6+ named modes) + forward-looking section (~500 từ) + outro. Then ship sprint wrap-up: README/CLAUDE/CHANGELOG/sign-off doc + tag.

**Requirements:** R3, R6, R7, R9, R10, R11, R12.

**Dependencies:** U2-U6 (final review pass + cross-cutting wrap-up).

**Files:**
- `docs/methodology.md` (modify — fill Friction + Forward-looking + outro)
- `README.md` (modify — current-stage block updated to point at methodology doc)
- `CLAUDE.md` (modify — current-stage block updated; sprint history Sprint-5 → methodology writeup)
- `docs/CHANGELOG.md` (modify — new entry for methodology writeup)
- `docs/phase-gates/2026-05-XX-methodology-writeup-signoff.md` (new sign-off doc)
- `docs/plans/2026-05-18-001-feat-methodology-writeup-plan.md` (this file; frontmatter `status: active → completed` at end)

**Approach:**

**Friction section structure** (~1000 từ, chronological ordering):
- Intro paragraph (50 từ): "Methodology has costs. Here are the named friction modes surfaced across 7 sprints."
- 6+ named modes, mỗi mode 150-200 từ:
  1. **Context window pressure mid-sprint** — Sprint-5 case; parent context burning fast with each subagent dispatch + review cycle. Subagent dispatch là partial fix.
  2. **Subagent re-dispatch khi usage limits hit mid-task** — Sprint-5 U8 case; subagent re-dispatched at session start with full context re-load. Cost: ~30% re-investigation. Mitigation suggested: more granular checkpoint commits.
  3. **.NET 9 SDK gap on dev machine** — global.json pins 9.0.305; dev machine has 8.0.407. Local `dotnet build` blocked Sprint-1-redux through Sprint-5. CI validates. Cost: longer feedback loop; can't local-iterate fast.
  4. **Skip'd deferral pattern — kicking the can?** — Sprint-4 U9 + Sprint-5 U9 examples. Honest case-by-case: legit gap (harness complexity) vs cargo-cult (deferring without justification). Cost: Sprint-5.5 not yet built — sprint debt accumulates.
  5. **KTD7 mid-sprint emergence** — Sprint-5 U7 case. Could have surfaced earlier with explicit tenant-scope question in U3 plan-time. Lesson: plan-time questions about singleton-vs-scoped + tenant-context boundary should be standard.
  6. **CRLF/LF line-ending noise per commit** — Windows dev + Linux CI; every commit shows "warning: LF will be replaced by CRLF". Cosmetic but constant signal-noise. Mitigation: `.gitattributes` config (not yet applied to project).
- Optional 7th mode: **Doc inventory growth** — AGENTS.md, CLAUDE.md, sign-off docs all grow over time. Session-start context load cost increases.

**Forward-looking section structure** (~500 từ):
- Open questions chỉ surface bằng việc viết doc này
- What would be different next time:
  - Earlier KTD7-style discovery: explicit "scope/lifetime/tenant-context" checkpoint in every plan-time review
  - Granular checkpoint commits: smaller commits per subagent task để re-dispatch cheaper
  - `.gitattributes` config at project init: avoid CRLF noise
  - More Mermaid diagrams trong plans for complex flows: prose-only worked but visual companion HTML proved valuable mid-sprint
- Process improvements deferred to follow-up sprint (note: not THIS sprint scope per R12)
- Public blog derivative deferred (not THIS sprint scope)

**Outro** (~100 từ):
- 1 paragraph "This doc is one snapshot. Project sau may surface different friction modes. Future-self updates this doc when they spot new patterns."
- Closing R11 reminder: one project, one solo dev, one stack.

**README + CLAUDE.md current-stage update**:
- Update current-stage block: "Phase-3 methodology writeup complete (2026-05-XX)" — describes the artifact + links to it
- Sprint history shifts: Sprint-5 paragraph → moved to history; new top paragraph points at methodology doc
- Mirror pattern from Sprint-4.5 → Sprint-5 history transition.

**CHANGELOG entry**:
- New section `## 2026-05-XX — Phase-3 Methodology Writeup complete`
- Tag `v0.8.0-methodology-writeup`
- Lists: artifact shipped (docs/methodology.md ~7000-9000 từ), 5 KTDs (single-file, chronological+synthesis, heavy linking, full honest, sparing Mermaid)
- Cross-references sign-off doc

**Sign-off doc** at `docs/phase-gates/2026-05-XX-methodology-writeup-signoff.md`:
- Mirror Sprint-5 sign-off shape
- Per-unit status table (U1-U7)
- KTD recap
- Deviations from plan
- Word count: actual final number
- Next-step pointer (Sprint-5.5 / blog derivative / Sprint-6)

**Plan frontmatter update**: `status: active → completed`, add `completed: 2026-05-XX`, `signoff: docs/phase-gates/...`, `tag: v0.8.0-methodology-writeup`.

**Patterns to follow:**
- Sprint-5 sign-off doc — overall sign-off structure.
- Sprint-4.5 → Sprint-5 README/CLAUDE current-stage transition pattern.

**Verification:**
- Friction section: 1000-1200 từ; 6+ named modes; each mode has Cost + Mitigation.
- Forward-looking section: 400-600 từ; lists open questions explicitly.
- Outro: 100-150 từ; closes with R11 reminder.
- `docs/methodology.md` total word count: 7000-9000 từ (sum across all sections).
- README current-stage block updated; old Sprint-5 paragraph moved to history.
- CLAUDE.md current-stage block updated.
- CHANGELOG has new section dated correctly.
- Sign-off doc created at correct path with correct frontmatter.
- Plan frontmatter: `status: completed`.
- Tag `v0.8.0-methodology-writeup` exists (annotated).
- `git diff main..HEAD --name-only` shows only `docs/` + `README.md` + `CLAUDE.md` changes (no `src/`).

---

## System-Wide Impact

| Surface | Impact | Owning unit |
|---|---|---|
| **`docs/methodology.md`** | New artifact, top-level docs path; serves as canonical AI-methodology reference | U1-U7 |
| **README.md** | Current-stage block updated to point at methodology doc; Sprint-5 paragraph moved to history | U7 |
| **CLAUDE.md** | Current-stage block updated; Sprint-5 history block preserved; new "Phase-3 methodology writeup complete" header block | U7 |
| **docs/CHANGELOG.md** | New section dated 2026-05-XX for methodology writeup | U7 |
| **docs/phase-gates/** | New sign-off doc for methodology writeup sprint | U7 |
| **docs/plans/** | This plan file frontmatter updated to `completed` at sprint close | U7 |
| **No `src/` modifications** | Sprint output is entirely `docs/` + project-root README/CLAUDE | (none) |
| **No test project changes** | No new tests; verification per unit is doc-quality checks | (none) |
| **No CI workflow changes** | Existing CI continues to validate; no methodology-specific build/test steps | (none) |
| **Git tag** | New annotated tag `v0.8.0-methodology-writeup` at sprint close | U7 |

---

## Dependencies / Prerequisites

- **Branch from `v0.7.0-sprint-5`** — cut new branch `feat/phase-3-methodology-writeup` (already at this state).
- **All sign-off docs read** — U2-U5 reference 8 sign-offs; ensure each is accessible + accurate.
- **All plan docs read** — U2-U5 reference 8 plan docs; ensure each is in `docs/plans/`.
- **All solutions/ docs read** — U2-U5 reference 5 institutional learnings.
- **All ADRs read** — U2 references ADR-0003 (DB-per-tenant pivot).
- **Git tag list verified** — 9 tags referenced; ensure all 9 tags exist locally + remote.
- **CLAUDE.md "Bootstrap stance" section read** — U2 references it; U7 updates current-stage block.
- **README.md "Current stage" section read** — U7 updates it; preserve other sections.
- **Word count tool** — implementer may want `wc -w` for verification per unit; not strict requirement (manual rough count OK).
- **Markdown preview** — implementer verifies TOC anchors + link integrity via preview tool (VS Code markdown preview or similar).
- **No external dependencies** — no new packages, no new tooling.

---

## Risk Analysis & Mitigation

| Risk | Likelihood | Impact | Mitigation |
|---|---|---|---|
| **Doc grows beyond 9000 từ target** — bloat undermines readability | Medium | Medium | Per-unit word count verification (U1-U7); if any section runs > 20% target, condense or move to appendix. |
| **Honesty section reads like complaint** — undermines credibility | Low | High | Each friction mode framed as "Pattern + Cost + Mitigation"; not "this sucked". Tone: surgeon describing complications, not patient venting. |
| **Self-deception about success** — doc whitewashes friction unconsciously | Medium | High | KTD4 explicit; AE2 requires 6+ named friction modes with concrete examples; final review pass specifically asks "if a skeptical reviewer asked 'what doesn't this doc tell me?', what would they find?". |
| **Sprint-5.5 unfinished gives doc unfinished feel** | Medium | Low | Deferral pattern subsection explicitly frames Sprint-5.5 as known follow-up cost; honest about not-yet-built status. |
| **Reader fatigue at 8000+ từ** | High | Medium | Hand-rolled TOC + clear subsection structure; reader can skim TOC and jump to relevant pattern. Friction section near end so casual reader can skip if uninterested. |
| **Link rot when files rename/move** | Low | Medium | Repo-relative paths (R7); if files move, single grep finds all references. Avoid line-number references unless stable. |
| **Subjective tone disagreement** — future-self thinks current tone wrong | Medium | Low | Doc is snapshot; future-self updates with new context. Outro explicitly notes "this doc is one snapshot". |

---

## Documentation Plan

- `docs/methodology.md` — primary deliverable.
- `README.md` — secondary update (current-stage block).
- `CLAUDE.md` — secondary update (current-stage block + sprint history block).
- `docs/CHANGELOG.md` — entry for methodology writeup.
- `docs/phase-gates/2026-05-XX-methodology-writeup-signoff.md` — sign-off doc.
- `docs/plans/2026-05-18-001-feat-methodology-writeup-plan.md` — this file; status updated at close.

No other docs touched.

---

## Operational / Rollout Notes

- This sprint has no code rollout. Output is documentation; "rollout" is git push of doc commits.
- No deployment topology change.
- No tenant provisioning impact.
- No feature flag.
- CI builds existing test suite; no methodology-specific CI steps.

---

## Future Considerations

These items are out of scope but materially affect what comes after:

- **Public blog post derivative** (~3000-4000 từ adapted for external reader) — separate brainstorm/plan/work cycle. Target: `docs/blog/` directory or external post URL.
- **Process improvements based on findings** — reflection sprint that revises AGENTS.md / skill cadence based on what writing the methodology doc surfaced. Particularly: KTD7-style emergence — should plan-time have explicit checkpoints for "scope/lifetime/tenant-context" decisions?
- **Reusable template repo** — if external blog feedback shows demand, extract AGENTS.md + ADR template + sprint cadence into `template-claude-code-project` repo. Not committed.

---

## Outstanding Questions

### Resolve Before Implementation

*(rỗng — toàn bộ plan-time decisions captured trong KTD1-6)*

### Deferred to Implementation

- [Affects U1, U7][Technical] Hand-rolled TOC format: `[Section](#section-slug)` Markdown style hay HTML `<a name="...">` anchors? Plan recommend Markdown style; implementer verifies in markdown preview tool.
- [Affects U6][Technical] Mermaid syntax: implementer chọn `flowchart` vs `sequenceDiagram` cho 2 diagrams. Plan recommend `flowchart LR` for cadence loop + `flowchart TD` for KTD discovery cycle (more compact).
- [Affects U2-U5][Technical] Commit hash references: short SHA (7 chars) per KTD3. Implementer captures: which commits per sprint to reference? Plan suggests: representative commit per sprint (1-2 per sprint, the "anchor" commit like U10 sign-off commit).
- [Affects U7][Technical] Sign-off doc date: 2026-05-XX placeholder — implementer fills with actual completion date.
- [Affects U7][Technical] `.gitattributes` config for CRLF — implementer may add as a fix when surfacing friction mode 6 (CRLF noise), or defer to follow-up reflection sprint. Plan recommend: defer (not THIS sprint scope per R12).
- [Affects U6][Needs review] Subsection 1 cadence Mermaid: should it show single sprint loop (brainstorm → plan → work → sign-off) or multi-sprint loop with tag dependency? Implementer decides based on readability test.
