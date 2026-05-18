---
date: 2026-05-18
topic: methodology-writeup
---

# Methodology Write-up — `docs/methodology.md`

## Summary

Ship `docs/methodology.md` — comprehensive 7-sprint chronological case study (~7000-9000 từ) ghi lại AI-assisted development methodology dùng trên ShopFlow WMS, full honest về cả patterns đã work lẫn friction surfaced. Durable artifact để self-validate "chứng minh dùng được AI hiệu quả" + cho dev khác clone repo đọc. Public blog derivative defer follow-up sprint.

---

## Problem Frame

ShopFlow WMS đã ship 7 sprints (Phase-0-redux → Sprint-5) sử dụng Claude Code + ce-brainstorm / ce-plan / ce-work skill cadence. Toàn bộ evidence của methodology đã rải khắp repo: 4 brainstorm docs, 6 plan docs, 8 sign-off docs, ADRs, solutions/, AGENTS.md, CLAUDE.md, 50+ commits với conventional messages + KTD discovery patterns. Nhưng nó **chưa được synthesize thành một artifact đọc được**.

Without writeup:
- Bản thân 6 tháng sau sẽ phải re-derive methodology từ scattered evidence
- Dev khác clone repo phải đọc rất nhiều file để hiểu pattern
- "Chứng minh dùng được AI hiệu quả" thiếu một place để point reader tới
- Friction modes (context window pressure, Skip'd deferrals, KTD7 emerging mid-sprint) không có nơi đề rút bài học cho project sau

Project's primary deliverable đã shift: WMS code không còn là main artifact — methodology proof IS. Sprint kế tiếp ship cái artifact đó.

---

## Actors

- A1. **Future-self (6 tháng sau)** — primary reader. Cần re-load methodology nhanh để áp dụng cho project sau. Đọc trong repo, không phải web.
- A2. **Dev khác clone repo** — secondary reader. Curious về "how to use AI for serious projects". Đọc qua GitHub UI hoặc local clone.
- A3. **Project author (hiện tại)** — writer. Synthesize 7 sprints worth of scattered evidence vào coherent narrative.

---

## Requirements

**Document shape**
- R1. Ship file `docs/methodology.md` trong repo. Single markdown file, không split multi-doc.
- R2. Length target ~7000-9000 từ. Mỗi sprint section ~500-800 từ; synthesis section cuối ~2000-3000 từ; friction section ~1000 từ.
- R3. Structure: lead-in / context section → 7 chronological sprint sections → synthesis (pattern catalog) → friction (what didn't work + costs) → forward-looking notes.

**Content coverage**
- R4. Each sprint section covers: what was built (1-2 sentence summary), KTDs encountered (planned + emergent), deferrals (Skip'd slots, scope cuts), what worked / what surfaced friction. Reference exact file paths + commit hashes + sign-off docs.
- R5. Synthesis section organized by reusable pattern category: (a) ce-brainstorm / ce-plan / ce-work cadence, (b) KTD discovery (mid-plan + mid-sprint emergence), (c) subagent dispatch (serial vs inline trade-offs), (d) deferral pattern (Sprint-4 → 4.5 → Sprint-5 U9 → Sprint-5.5), (e) context management (AGENTS.md / CLAUDE.md / session-resume hooks).
- R6. Friction section covers honestly: context window pressure mid-sprint, subagent re-dispatch khi hit usage limit, .NET 9 SDK gap trên dev machine, Skip'd deferral pattern (kicking the can analysis), KTD7-emerged-mid-sprint (sao không surface sớm hơn?), CRLF/LF line-ending noise, sub-tier reviewers (read code → give up if complex), cost of process overhead per sprint.

**Reference density**
- R7. Heavy linking density. File paths repo-relative throughout. Commit hashes for representative units. ADR links (ADR-0001/0002/0003). Sign-off doc links per sprint. `docs/solutions/` entries linked at relevant friction points.
- R8. Code snippets sparing — only when pattern is hard to describe in prose (e.g., the conditional CTE INSERT shape, the K12 per-tenant DbContext binding pattern). Prefer file + line range references over inline blocks > 15 lines.

**Tone + honesty**
- R9. Tone: credible case study, not marketing. Acknowledge costs alongside benefits. No phrases như "AI made this 10x faster" without measurement; instead "Sprint-5 ran 10 implementation units in N sessions over X days; without subagent dispatch the same scope would have hit context window limits at unit Y".
- R10. Anti-patterns / kicking-the-can openly discussed: Sprint-5 U9 Skip'd 2 scale-gate slots; document the rationale + the honest assessment.

**Scope discipline**
- R11. Document does NOT advocate for the methodology as universally applicable. Acknowledges this is one project, one solo dev, one stack (.NET 9 + Postgres + modular monolith).
- R12. No new code in `src/`. Sprint output is entirely in `docs/`. Tag `v0.8.0-methodology-writeup` at close.

---

## Acceptance Examples

- AE1. **Covers R3, R4.** Given the doc is structured per the template, when a reader scrolls through, they see 7 chronological sprint sections (Phase-0-redux → Sprint-1-redux → Sprint-2-redux → Sprint-2.5 → Sprint-3-redux → Sprint-4 → Sprint-4.5 → Sprint-5) — each with KTDs, deferrals, friction notes, and links to the relevant sign-off doc. The synthesis section organizes patterns by category, not by sprint.
- AE2. **Covers R6, R9.** When a reader hits the friction section, they see at least 6 named friction modes with concrete examples (e.g., "Sprint-5 U8 subagent re-dispatched mid-flight when usage limit hit; the re-dispatched agent had to re-investigate 30% of the context the original had collected"). Reader does not get the impression methodology is friction-free.
- AE3. **Covers R7.** When a reader clicks a file path link, it points at an actual file in the repo. When they click a commit hash, GitHub resolves it. When they click an ADR link, the ADR file exists.
- AE4. **Covers R10.** Sprint-5 U9 section explicitly discusses the 2 Skip'd scale-gate slots: rationale, precedent (Sprint-4 U9), honest cost ("Sprint-5.5 follow-up sprint required to close measurement gap"). Doc does NOT pretend the gate was filled.
- AE5. **Covers R12.** `git diff main..HEAD --name-only` after sprint close shows only `docs/` changes (no `src/` modifications). `README.md` + `CLAUDE.md` "Current stage" sections updated to point at the new doc.

---

## Success Criteria

- Future-self 6 tháng sau đọc `docs/methodology.md` end-to-end (~30-45 phút) và có thể start project mới with reusable patterns (cadence, AGENTS.md template, KTD discipline, deferral mechanic).
- Dev khác clone repo, đọc README → click vào `docs/methodology.md` → hiểu được high-level "how this project was built with AI" trong 15 phút (skim) hoặc 45 phút (deep read).
- Friction modes documented đủ để dev khác cân nhắc trade-offs trước khi áp dụng methodology cho project của họ — NO false confidence.
- Sprint-3 (planning skill) có thể đọc doc + draft kế tiếp methodology iteration mà không cần phỏng vấn người viết.
- Tag `v0.8.0-methodology-writeup` exists; CHANGELOG entry; README + CLAUDE.md "Current stage" updated.

---

## Scope Boundaries

- **WMS feature work** — Sprint-5.5 scale gate, Sprint-6 Analytics, deployment doc, live demo URL, Gateway hardening, observability dashboards. Tất cả defer hoặc reject.
- **Reusable template repo extraction** — không build `template/` directory hoặc spin-off repo. Methodology lives ngay trong shopflow-wms repo.
- **HR / recruiter-targeting README hero pitch** — current README giữ nguyên (technical, dense). Hero pitch for HR scan defer indefinitely (out of project identity).
- **Public blog post / dev.to publication** — defer sang follow-up sprint riêng. THIS sprint chỉ ship internal `docs/methodology.md`.
- **Process improvements to AGENTS.md / skill cadence dựa trên findings** — có thể là output of next reflection cycle, nhưng không trong scope sprint này. Doc note xuống Outstanding Questions.
- **Code snippets longer than ~15 lines** — prefer file path + line range reference instead.
- **Comparative analysis vs other AI-coding methodologies** (e.g., Cursor + custom rules, Aider, Codex, etc.) — acknowledged as out of scope. Doc speaks only to ShopFlow WMS evidence.
- **Quantitative measurements** beyond what's already in sign-off docs (e.g., test counts, commit counts, KTD counts) — không thêm new measurement project; reuse existing numbers.

---

## Key Decisions

- **Single-file methodology doc, not split** — chosen vì 1 file dễ link, dễ navigate trong markdown viewer, future-self search-friendly. Multi-doc structure (e.g., `docs/methodology/cadence.md`, `docs/methodology/deferral.md`) defer nếu doc bloat quá khả năng đọc 1 lần.
- **Chronological narrative + synthesis section, NOT pattern-catalog-only** — chosen vì comprehensive coverage (đã chốt) cần story arc; pattern catalog alone mất chronology + causality (e.g., KTD7 emergence chỉ make sense if Sprint-3-redux K12 pattern đã được established earlier).
- **Full honest about friction** — chosen vì credibility là precondition cho self-validate goal. Success-only doc reads như marketing và mất audience trust.
- **No new code in `src/`** — sprint output is `docs/` only. Tag bumps minor version to `v0.8.0-methodology-writeup` to mark a non-feature deliverable.
- **Public blog derivative deferred** — chosen vì internal doc là source of truth; derivative cần adapt language + add hook for cold reader; mixing concerns trong cùng sprint dilutes both. Follow-up sprint riêng có scope rõ.

---

## Dependencies / Assumptions

- Tất cả sign-off docs (`docs/phase-gates/*-signoff.md`) đã exist và accurate — methodology doc references chúng heavily.
- Tất cả plan docs (`docs/plans/*.md`) đã exist và phản ánh đúng KTD + deviations — synthesis section reuses these.
- Tất cả brainstorm docs (`docs/brainstorms/*.md`) — chronological reference cho từng sprint.
- `docs/solutions/` entries — reference khi describe friction patterns (e.g., EF migration silent no-op, multi-row CTE concurrency).
- Git history clean với conventional commits — methodology doc references commit hashes for representative units.
- AGENTS.md + CLAUDE.md — describe context configuration patterns; doc references chúng.
- No external dependencies added (no new packages, no new tools).

---

## Outstanding Questions

### Resolve Before Planning

*(không có — toàn bộ product decisions đã chốt qua dialogue)*

### Deferred to Planning

- [Affects R1, R2][Technical] File layout: doc placed at `docs/methodology.md` (top of docs/) hay `docs/methodology/README.md` (subfolder cho future expansion)? Plan quyết based on whether multi-file split likely.
- [Affects R7, R8][Technical] Markdown anchors / TOC generation: hand-rolled vs auto-generated? `[![Stage]]` badge tương tự README? Plan quyết.
- [Affects R5][Needs research] Pattern catalog có cần Mermaid diagrams không? E.g., cadence flow diagram, KTD discovery cycle. Plan đọc các doc tương tự + decide.
- [Affects R4][Technical] Commit hash references: short hash (7 chars) hay full SHA? Standard practice và what GitHub renders cleanly.
- [Affects R6][Technical] Friction section ordering: chronological (cùng story arc) hay severity-ranked (most-impactful first)? Plan thử cả 2 và decide.
- [Affects R12][Process] Public blog derivative — separate brainstorm/plan/work cycle sang follow-up sprint hay treat như U-N+1 trong sprint này? Plan recommend defer khi viết.

### Deferred to Follow-up Sprint (separate scope)

- **Public blog post derivative** (~3000-4000 từ, target dev.to / personal blog) — standalone derivative đọc-không-cần-clone-repo. Brainstorm + plan riêng sau khi internal doc settled.
- **Process improvements to AGENTS.md / skill cadence** — based on findings từ writing the methodology doc. Reflection cycle riêng.
- **Reusable template repo extraction** — chỉ nếu external blog feedback cho thấy đủ demand. Not committed.
