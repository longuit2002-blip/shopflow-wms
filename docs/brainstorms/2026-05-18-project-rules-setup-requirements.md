---
date: 2026-05-18
topic: agents-md-ktd-foldin
---

# AGENTS.md — Fold KTD Sprint-1→5

## Summary

Bổ sung 6 rule vào AGENTS.md để fold các Key Technical Decision đã học được Sprint-1→5 (K11, K12, K13, K15, KTD1, KTD7). Hiện các KTD này chỉ sống ở CLAUDE.md + `docs/solutions/` — không có trong rule canon mà AI agent thật sự anchor khi viết code.

---

## Problem Frame

Mỗi sprint xong xuôi, các bài học load-bearing được capture đúng chỗ: CLAUDE.md ghi context dài, `docs/solutions/` ghi giải pháp chi tiết. Nhưng **AGENTS.md là rule canon được AI agent (Claude/Cursor/Copilot) anchor khi sinh code** — và 6 KTD Sprint-1→5 chưa landing ở đó. Rủi ro cụ thể: Sprint-6 Analytics sẽ chạm vào outbox route registry (K13), per-tenant DbContext binding (K12), explicit `tenantId` trên port từ singleton hosted service (KTD7) — đúng các pattern AI agent từng làm sai lần đầu và đã được sửa bằng review back-and-forth. Không có rule, AI agent re-discover lại pattern, tốn review cycle.

Scope brainstorm bị cắt từ "full project rule setup" về đúng pain quan sát được sau khi user pressure test — phần CQRS rules, frontend scaffold, hook config, install global rules là speculative hoặc giải sai cách.

---

## Requirements

- R1. Thêm rule mới vào AGENTS.md `## 3. Multi-tenancy and data access`: **K12 + KTD7 — port gọi từ singleton context phải nhận `Guid tenantId` explicit**. Singleton wrapper (consumer, hosted service, multiplexed dispatcher) mở scope DI + bind `IRequestContext` qua `ITenantCatalog.LookupByIdAsync` trước khi delegate sang scoped inner repo. Saga DbContext: dùng `TenantBindingSagaFilter<T>` (primary) + `TenantAwareSagaDbContextFactory<T>` (fallback).
- R2. Thêm rule mới vào AGENTS.md `## 3`: **K11 — multi-row CTE INSERT/UPDATE đặt predicate trong UPDATE clause + dùng `NOT EXISTS` gate trên all-succeeded set**. Không pre-check ở CTE riêng (race window dưới READ COMMITTED). Reference: `docs/solutions/2026-05-13-multi-row-cte-predicate-must-live-in-update.md`.
- R3. Thêm rule mới vào AGENTS.md `## 6. Outbox, messaging, and idempotency`: **K13 — outbox route registry pattern**. Module gọi `services.AddOutboxRoute<T>(SendKind, destination?)` khai báo route command-vs-event; `MultiplexedOutboxDispatcher` đọc registry per row. Mặc định = `OutboxRoute.PublishDefault` (kebab-case CLR type name).
- R4. Thêm rule mới vào AGENTS.md `## 6`: **KTD1 — emit single canonical event mang state cuối khi nhiều domain transition converge về cùng downstream state**. Heuristic: nếu consumer phải re-map line→sku hay re-query downstream để dựng state, event signal sai. Reference cụ thể: `StockLevelChangedV1` thay 3 event `StockReservedV1`/`StockReleasedV1`/`StockConfirmedV1`.
- R5. Thêm rule mới vào AGENTS.md `## 11. Module shape canon`: **K15 — MassTransit.EntityFrameworkCore 8.3.4 pin** cho saga repository khi project chạy EF Core 9. Pin trong `Directory.Packages.props`; nâng version phải test full saga flow trước.
- R6. AGENTS.md mở đầu mỗi rule mới cite `docs/solutions/...` hoặc CLAUDE.md section khi có, để rule executable và có thể truy về câu chuyện gốc. Không cite ADR (6 rule này đều không tạo ADR mới — analyzer chưa enforce).

---

## Success Criteria

- AGENTS.md tăng từ 83 rule lên 89 rule (≤ 200 budget); diff đọc trong 5 phút và scan-resistant
- Khi Sprint-6 Analytics landing và AI agent viết command/handler đầu tiên cho CQRS read-side, paste AGENTS.md vào prompt → rule K12/KTD7 catch ngay nếu hosted-service-driven handler bỏ quên `tenantId` parameter
- Mỗi rule mới link tới chứng cứ (solutions/ doc hoặc CLAUDE.md mục cụ thể) — agent verify được khi nghi ngờ

---

## Scope Boundaries

- KHÔNG copy `~/.claude/rules/common/*` hay `~/.claude/rules/csharp/*` vào `docs/rules/` — CLAUDE.md đã làm job đó
- KHÔNG viết CQRS rules tier-1 — defer Sprint-8 (commit đầu tiên có handler thật sẽ là dialogue tốt hơn)
- KHÔNG viết frontend scaffold — defer Sprint-7
- KHÔNG sửa `.claude/settings.json` (hooks + allowlist) — `/fewer-permission-prompts` là cách trị đúng cho permission pain; làm khi pain trở nên rõ ràng
- KHÔNG thêm mục `## 12` mới — 6 rule fit gọn vào 3 mục hiện có (§3, §6, §11)
- KHÔNG đụng tới 6 file `AGENTS.md` per-module — sprint deltas đã đủ
- KHÔNG audit toàn bộ AGENTS.md cho rule rot — chỉ fold 6 KTD mới

---

## Key Decisions

- **Cắt 14/20 requirement của draft đầu** sau pressure test: AI-drift là pain giả thuyết với 1 developer project; install rule library duplicate CLAUDE.md; CQRS/frontend speculative vì code chưa có; hook config có cách trị tốt hơn (transcript scan). Pain duy nhất concrete là AGENTS.md không có 6 KTD, áp lực rõ Sprint-6.
- **Mỗi rule có cite source**: AGENTS.md đã có precedent (rule 23 cite `docs/solutions/2026-05-10-ef-migration-needs-attributes.md`). Giữ chuẩn này — rule executable, không phải khẩu hiệu.
- **Không tạo mục `## 12` mới**: K11/K12/KTD7 thuộc §3 multi-tenancy/data access; K13/KTD1 thuộc §6 outbox; K15 thuộc §11 module shape. Phân loại đúng giúp scan, không dồn KTD vào "mục mới cho dễ".

---

## Dependencies / Assumptions

- AGENTS.md hard budget 200 instruction (rule 67) — sau pass này tổng còn dưới 90, không near limit
- 6 KTD nói trên đã verify là KHÔNG có trong AGENTS.md hiện tại (Read full file 160 dòng, session này)
- `docs/solutions/2026-05-13-multi-row-cte-predicate-must-live-in-update.md` đã tồn tại (CLAUDE.md cite, không cần verify Read lại)
- `Directory.Packages.props` pin MT.EFCore 8.3.4 (đã verify gián tiếp qua Sprint-3-redux KTD note trong CLAUDE.md)

---
