---
title: "Phase-3 Methodology Writeup sign-off — docs/methodology.md"
date: 2026-05-18
status: complete
follows: docs/phase-gates/2026-05-17-sprint-5-signoff.md
plan: docs/plans/2026-05-18-001-feat-methodology-writeup-plan.md
tag: v0.8.0-methodology-writeup
---

# Phase-3 Methodology Writeup sign-off — `docs/methodology.md`

Closes [`docs/plans/2026-05-18-001-feat-methodology-writeup-plan.md`](../plans/2026-05-18-001-feat-methodology-writeup-plan.md). A single-deliverable sprint shipping [`docs/methodology.md`](../methodology.md) — comprehensive 7-sprint chronological case study + synthesis pattern catalog + friction section + forward-looking section. No source code changes; output is 100% in `docs/` + `README.md` + `CLAUDE.md`. Seven implementation units shipped on `feat/phase-3-methodology-writeup` cut from `v0.7.0-sprint-5`.

## What shipped

| U-ID | Goal | Status | Words added |
|------|------|--------|-------------|
| U1 | Doc skeleton + lead-in + TOC + reference inventory (29 doc links + 10 git tags) | ✅ | ~1300 |
| U2 | Phase-0-redux + Sprint-1-redux sections | ✅ | ~990 |
| U3 | Sprint-2-redux + Sprint-2.5 + Sprint-3-redux sections | ✅ | ~1160 |
| U4 | Sprint-4 + Sprint-4.5 sections | ✅ | ~857 |
| U5 | Sprint-5 section (most-detailed; KTD1 + KTD7 + subagent dispatch + visual companion + .NET 9 SDK gap) | ✅ | ~860 |
| U6 | Synthesis pattern catalog — 5 patterns + 2 Mermaid diagrams + R11 closing | ✅ | ~2056 |
| U7 | Friction (7 named modes) + forward-looking + outro + README/CLAUDE/CHANGELOG/sign-off/tag | ✅ | ~1290 |
| | **Total** | | **~8500 words** |

## Final word count

| Section | Target | Measured | Note |
|--------|--------|----------|------|
| Lead-in + context | ~300 | ~430 | slight over from disclaimer + reader-expectation paragraph |
| 7 sprint sections (avg) | 500-800 each | 415-1100 each | Sprint-5 longest at ~1100; Sprint-2.5 shortest at ~330 (proportional to evidence) |
| Synthesis section (5 subsections) | ~2500 | ~2056 | slightly under target; cross-sprint evidence kept tight |
| Friction section | ~1000 | ~990 | 7 named modes — one more than min target |
| Forward-looking section | ~500 | ~480 | 4 process improvements + 4 open questions + items deferred |
| Outro + closing R11 | ~100 | ~110 | snapshot disclaimer + future-self update note |
| Appendix reference inventory | ~500 | ~870 | dense due to 29 doc links + 10 tags |
| **Total doc** | **7000-9000** | **~8510** | in target range |

## Key technical decisions (recap of plan KTDs)

- **KTD1 — Single-file `docs/methodology.md` at top-level**: chosen for navigability + future-self search-friendly. Multi-doc split (e.g., `docs/methodology/cadence.md`) deferred until doc bloats past readable-in-one-session threshold.
- **KTD2 — Chronological narrative first, then synthesis pattern catalog**: chosen because comprehensive coverage needs story arc + causality (KTD7 emergence only makes sense after K12 pattern is established earlier).
- **KTD3 — Heavy linking, sparing code snippets**: repo-relative file paths + short commit hashes (when referenced) + ADR + sign-off + solutions links throughout. Code snippets kept ≤ 15 lines.
- **KTD4 — Full-honest friction section**: 7 named friction modes with Pattern/Cost/Mitigation framing. No marketing phrases. Acknowledge Sprint-5 U9 kicking-the-can analysis openly.
- **KTD5 — Mermaid sparing**: 2 diagrams (cadence flow loop + KTD discovery cycle in synthesis section). Sprint-section flowcharts deliberately omitted — prose narrative is sufficient.
- **KTD6 — Verification per unit = word-count range + link integrity + content checklist**: no `*.cs` test files. Documentation sprint, not feature work.

## Deviations from plan

- **No new KTDs surfaced mid-execution** — the planning was exhaustive enough that all decisions fired at plan-time. This is rare and partly reflects the doc-writing-only nature of the sprint (no code constraints to surprise execution).
- **U6 synthesis came in slightly under target** (~2056 vs ~2500 target). Cross-sprint evidence kept tight rather than padded; each pattern has 3-5 evidence points rather than 6-8. Quality over quantity decision.
- **Appendix (U1) came in over target** (~870 words vs ~500 target). 29 doc links + 10 git tags + grouped sections naturally exceeded the budget; opted not to compress.
- **One additional friction mode (mode 7 doc inventory growth)** was added during U7 that wasn't explicitly in the plan's friction list. Surfaced when writing the context management synthesis subsection; included because it's a known future-cost not yet at threshold.
- **CLAUDE.md update inherited a duplicate sign-off line** during the Sprint-5-history relocation in U7; cleaned up in same commit.

## What this sign-off does NOT claim

- This methodology is universally applicable. It worked for **one project, one solo developer, one stack, one tool combination**. Future projects using it should re-validate assumptions on their own evidence.
- The friction modes documented are exhaustive. They're the ones that surfaced in 7 sprints; project-8 might surface mode 8.
- The deferral pattern is proven to scale beyond 3 cycles. Sprint-5.5 isn't yet built; if it doesn't close within a week as estimated, the pattern's evidence base weakens.
- The doc is a finished artifact. It's a snapshot at 2026-05-18; future-self will update when new patterns surface or old patterns turn out wrong.

## Compounding learnings landed

The doc itself is the institutional learning artifact. Specific patterns it codifies for future projects:

- **The four-stage cadence** (brainstorm → plan → work → sign-off) with its fractal application at point-release sprints (Sprint-N.5 closure cycles).
- **Plan-time read-actual-code checkpoint** as defence against brainstorm-level idealisation (Sprint-5 KTD1 + Sprint-4.5 R6 + Sprint-3-redux K11 all caught by this).
- **Subagent dispatch as context-isolation pattern** with honest cost accounting (~30% re-investigation overhead per subagent).
- **Deferral with named follow-up sprint** as scope-discipline mechanism, with the case-by-case kicking-the-can test as a guardrail.
- **Visual companion HTML as emergency context-management tool** when prose dialogue stops landing.

## Build/test invariants at close

- `dotnet build` — unchanged from `v0.7.0-sprint-5`. No source code modifications.
- `dotnet test --filter "Category!=Integration"` — unchanged (359 unit tests still passing per Sprint-5 sign-off).
- `dotnet test --filter "Category=Integration"` — unchanged.
- `dotnet test --filter "Category=Load"` — unchanged (2 Skip'd slots from Sprint-5 U9 still Skip'd; closure remains a Sprint-5.5 task).
- Doc word count: ~8510 words in `docs/methodology.md`, target 7000-9000 — in range.
- All link integrity verified (29 doc paths + 10 git tag references all resolve).

## Tag

`v0.8.0-methodology-writeup` — annotated tag at the U7 sign-off commit. Bumps minor version (0.7 → 0.8) to mark a non-feature deliverable; convention: methodology / docs-only sprints bump minor, not patch.

## Next implementation step

Cut a fresh branch from `v0.8.0-methodology-writeup` and start one of:

- **Sprint-5.5** — close U9 scale-gate harness gap (multi-tenant Aspire boot + real Shopee mock alongside StockSync.Api). Pattern proven by Sprint-2.5 / Sprint-4.5; estimated ~1 week.
- **Sprint-6** — Analytics module (W9-W10): read-side projections / dashboards consuming the existing outbox stream including `StockLevelChangedV1`.
- **Public blog derivative** — adapted ~3000-4000 word version of `docs/methodology.md` for dev.to / personal blog. Separate brainstorm + plan + work cycle.
- **Process improvements based on methodology findings** — `.gitattributes` for CRLF normalisation, plan-time port-shape checklist, granular checkpoint commits inside subagent runs. See methodology forward-looking section.
