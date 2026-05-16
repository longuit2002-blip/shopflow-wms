---
date: 2026-05-16
topic: sprint-5-stock-sync-engine
---

# Sprint-5 — Stock Sync Engine

## Summary

Sprint-5 ships `ShopFlow.StockSync` — module mới đẩy stock-change từ Inventory lên Shopee theo round-trip thật, với bốn cơ chế isolation đa-tenant (coalescing buffer, per-tenant token bucket, flash-sale priority queue, circuit breaker) đủ để pass noisy-neighbor scale gate (5 tenants × A burst 2k stock-change/s × 5 phút). Sau Sprint-5, project chỉ còn Sprint-6 Analytics và Phase-3 polish — cả hai được liệt kê high-level ở cuối doc để khép roadmap, brainstorm chi tiết sau.

---

## Problem Frame

ShopFlow WMS hiện đã có:
- Inventory module với reservation ledger atomic chống oversell (Sprint-1-redux)
- Inbound module ghi nhận stock thực tế từ GRN (Sprint-2-redux)
- Outbound module + fulfillment saga (Sprint-3-redux)
- Channel module webhook-ingress: marketplace → ShopFlow đã liền mạch (Sprint-4 + 4.5)

Phần thiếu duy nhất ở Phase-2 là **chiều ngược lại**: khi stock thật của một tenant thay đổi (do reserve / release / confirm / put-away), giá trị `available_to_sell` mới phải được đẩy lên các channel marketplace để không bán âm và không tồn ảo. Đây là chỗ phát sinh các vấn đề noisy-neighbor đặc trưng của multi-tenant: một tenant flash-sale có thể bắn 2k delta/giây và làm chết các tenant khác trên cùng marketplace nếu không có isolation.

Đây cũng là centerpiece "noisy-neighbor isolation" của portfolio — minh chứng vận hành đúng dưới tải lệch giữa các tenant trên cùng cluster.

---

## Actors

- A1. **Inventory module** — phát domain events khi stock state đổi (`StockReservedV1`, `StockReleasedV1`, `StockConfirmedV1`); là source-of-truth.
- A2. **StockSync engine** — consumer các event của A1, coalesce + queue + rate-limit + push lên marketplace adapter.
- A3. **Channel adapter (Shopee)** — wrap call HTTP tới marketplace; mock server (Sprint-4 U7) đứng làm Shopee thật.
- A4. **Tenant operator** — set `is_flash_sale` flag trên SKU campaign (qua admin API / seed-data); không có UI riêng trong Sprint-5.
- A5. **Reservation ledger (Sprint-1-redux)** — chịu trách nhiệm cuối cùng chống oversell khi nhiều order race trên cùng SKU; StockSync **không** can thiệp.

---

## Key Flows

- F1. **Stock-change → push lên marketplace** (đường nóng)
  - **Trigger:** Inventory commit một event trong `{StockReservedV1, StockReleasedV1, StockConfirmedV1}` cho SKU `S` của tenant `T`.
  - **Actors:** A1 → A2 → A3.
  - **Steps:** (1) StockSync consumer nhận event qua RabbitMQ; (2) tính lại `available_to_sell` cho SKU `S`; (3) ghi vào coalescing buffer per `(tenant, sku, channel)`; (4) coalescing window đóng → đẩy entry vào queue (high-priority nếu SKU có `is_flash_sale=true`); (5) dispatcher rút khỏi queue tuân thủ token bucket per `(tenant, channel)`; (6) Shopee adapter push HTTP; (7) thành công → log audit row; thất bại → đếm vào circuit-breaker counter.
  - **Outcome:** Mỗi marketplace luôn thấy `available_to_sell` mới nhất trong giới hạn rate-limit cho phép; không có push lỗi đè lên nhau; flash-sale SKU không bị xếp sau các delta thường.
  - **Covered by:** R1, R2, R3, R4, R5, R6, R7, R10.

- F2. **Noisy-neighbor isolation dưới burst**
  - **Trigger:** Tenant A bắn liên tục 2000 stock-change/giây trong 5 phút trên 1 SKU flash-sale; tenants B-E vẫn có traffic thường ~10 delta/giây.
  - **Actors:** A1 (×5 tenants) → A2 → A3.
  - **Steps:** (1) Mỗi tenant có buffer, queue, token bucket, breaker **riêng**; (2) Token bucket per (tenant, channel) đảm bảo A không tiêu hết quota của B-E; (3) Coalescing biến 2000 delta/s của A thành ≤ token-bucket-rate push thực; (4) Breaker chỉ trip cho cặp (A, Shopee) khi marketplace fail, không lan sang B-E.
  - **Outcome:** B-E giữ p99 < 30s end-to-end; per-tenant fairness floor ≥ 0.85 (định nghĩa theo `FairnessCalculator` của Sprint-1-redux/Sprint-4.5).
  - **Covered by:** R8, R9.

- F3. **Breaker tripping + recovery**
  - **Trigger:** Shopee mock chaos endpoint bật 5xx liên tục cho (tenant `T`, channel = Shopee).
  - **Actors:** A2 ↔ A3.
  - **Steps:** (1) Đủ số lỗi liên tiếp → breaker `(T, Shopee)` chuyển Open; (2) Push của T bị reject ngay tại engine, không tạo HTTP call; (3) Sau thời gian cooldown → Half-Open thử 1 call; (4) Thành công → Closed lại; tiếp tục thất bại → Open thêm chu kỳ.
  - **Outcome:** Marketplace không bị bom tiếp khi đang lỗi; các tenant khác không bị ảnh hưởng.
  - **Covered by:** R6, R9.

---

## Requirements

**Stock-change ingestion**
- R1. StockSync consume `StockReservedV1`, `StockReleasedV1`, `StockConfirmedV1` qua MassTransit/RabbitMQ với consumer idempotent (dedup theo `event_id`).
- R2. Khi nhận event của tenant `T`, SKU `S`, engine tính lại `available_to_sell` cho `S` ở thời điểm đó (đọc snapshot từ Inventory port; không tự duy trì state stock).

**Coalescing**
- R3. Engine duy trì coalescing buffer per `(tenant, sku, channel)`. Trong cửa sổ coalescing, chỉ giá trị `available_to_sell` mới nhất được giữ; các giá trị cũ bị overwrite, không xếp hàng.
- R4. Cửa sổ coalescing cấu hình được per tenant; có default chung; cấu hình ở cấp config server, không cần redeploy code (đọc qua `StockSyncOptions`).

**Allocation**
- R5. Mỗi channel của tenant nhận đúng giá trị `available_to_sell` đã tính ở R2 (mirror-all). Engine **không** chia lại stock theo channel. Race-on-reserve được xử lý ở reservation ledger Sprint-1-redux.

**Rate-limit + breaker**
- R6. Engine duy trì token bucket per `(tenant, channel)` với sustain-rate + burst-size cấu hình được; dispatcher chỉ rút khỏi queue khi token đủ.
- R7. Engine duy trì circuit breaker per `(tenant, channel)` (Polly v8 ResiliencePipeline). Khi Open, push của cặp đó bị reject ngay tại engine; không gửi HTTP. Trip-threshold + half-open cooldown cấu hình được.

**Priority**
- R10. SKU có flag `is_flash_sale=true` (đọc từ Channel module's `ProductMapping` hoặc `StockSync.SkuFlag` table) được đẩy vào high-priority queue per tenant. SKU thường vào normal-priority queue. Dispatcher rút high trước normal, mỗi tenant có cặp queue riêng.

**Push**
- R11. StockSync gọi `IChannelAdapter.PushStockUpdate` của Channel module (Sprint-4 đã có stub) — round-trip thật qua Shopee mock server. Sprint-5 ship implementation thật cho Shopee adapter `PushStockUpdate`.
- R12. Mỗi lần push (success hoặc fail terminal) ghi audit row vào DB của StockSync module (`stock_sync_push_log` per tenant).

**Module shape**
- R13. Module mới `ShopFlow.StockSync` quartet (Domain / Application / Infrastructure / Api) theo cadence Sprint-2/3/4, có DbContext + migration riêng với prefix bảng `stock_sync_*` (Sprint-2.5 per-module-prefix canon).
- R14. Per-tenant DbContext binding qua K12 pattern (`TenantBindingSagaFilter` / `TenantAwareSagaDbContextFactory` của Sprint-3-redux); consumer + BackgroundService bind tenant context từ message header.

**Scale gate**
- R8. `Category=Load` test "noisy-neighbor 5 tenants": tenant A burst 2000 stock-change/s × 5 phút; B-E giữ p99 end-to-end < 30s; per-tenant fairness floor ≥ 0.85.
- R9. `Category=Load` test "breaker recovery": Shopee mock bật chaos 5xx → breaker (A, Shopee) trip → cooldown qua → push của A phục hồi mà không ảnh hưởng B-E.

**Sprint deliverable**
- R15. Sprint-5 kết thúc với tag `v0.7.0-sprint-5`, sign-off doc tại `docs/phase-gates/`, CHANGELOG entry, README + CLAUDE.md update — cadence như Sprint-4.5.

---

## Acceptance Examples

- AE1. **Covers R3, R4.** Cho coalescing window 500ms. Khi Inventory phát 10 event `StockReservedV1` cho cùng `(T1, SKU-X)` trong 500ms, engine push **1** giá trị `available_to_sell` mới nhất lên Shopee — không phải 10.
- AE2. **Covers R5.** Cho `(T1, SKU-X)` available = 7. Tenant T1 cấu hình channels = `{Shopee, Lazada, TikTok}` (Lazada/TikTok stub). Engine push value = 7 cho **tất cả** channel; không chia 3/2/2.
- AE3. **Covers R6.** Cho token bucket `(T1, Shopee)` = 10 RPS sustain + 50 burst. Khi queue có 200 entry sẵn sàng push, dispatcher gửi ≤ 50 trong giây đầu rồi ≤ 10 ở các giây tiếp theo — không vượt.
- AE4. **Covers R7, R9.** Cho breaker trip-threshold = 5 lỗi 5xx liên tiếp trong 30s. Khi Shopee mock trả 5xx cho 5 push liên tiếp của `(T1, Shopee)`, push tiếp theo bị reject ngay tại engine (không có HTTP call); sau cooldown 60s, 1 probe đi qua; probe thành công → trở lại Closed.
- AE5. **Covers R10.** Cho queue (T1, Shopee) đang có 100 entry normal và 1 entry flash-sale-SKU. Dispatcher rút entry flash-sale trước, dù entry đó vào queue sau.
- AE6. **Covers R8.** Trong scale-gate test, sau 5 phút burst của tenant A, log push của B-E cho thấy p99 latency end-to-end < 30 giây và fairness ratio (min push/max push tính theo tenant) ≥ 0.85.

---

## Success Criteria

- Phase-2 đóng hoàn chỉnh: ingress (Sprint-4/4.5) + egress (Sprint-5) liên thông; portfolio có thể demo một dòng "marketplace order → reserve → ship → stock đẩy ngược lại tất cả channel" end-to-end qua mock.
- Scale gate noisy-neighbor pass: 5 tenants × A burst 2k/s × 5 phút × B-E p99 < 30s × fairness ≥ 0.85 (đo bằng CI nightly trên môi trường có Docker).
- Sprint-6 planner / Phase-3 planner có thể đọc requirements / sign-off / CHANGELOG entry mà không cần phỏng vấn người viết để hiểu Stock Sync đang làm gì và đã ship đến đâu.
- Toàn bộ test suite hiện tại (288 unit + integration + load) tiếp tục xanh; +N test mới của Sprint-5 không break test cũ.

---

## Scope Boundaries

- Lazada / TikTok adapters + mock servers — defer Phase-3.
- Auto-detect flash-sale từ burst rate, velocity-based allocation, reserve-buffer-per-channel allocation — không build.
- Admin UI cho toggle `is_flash_sale` — Sprint-5 chỉ qua seed-data hoặc admin API stub; UI riêng không có trong scope portfolio này.
- Real Shopee production API (credentials thật) — luôn chỉ mock.
- E2E chaos test bài bản (50% 5xx + 500ms latency liên tục) — chaos endpoint có sẵn, nhưng test bài bản defer.
- Sprint-6 Analytics module (read-side projection, dashboards) — brainstorm + plan riêng sau khi Sprint-5 đóng.
- Phase-3 polish (Gateway hardening, observability dashboards Grafana/Prometheus, portfolio README/demo video, deployment docs) — brainstorm + plan riêng.
- Multi-region / DR / backup-restore của StockSync state — out of scope (Phase-3+).

---

## Key Decisions

- **Mirror-all allocation (chọn ở Phase 2)**: cùng `available_to_sell` lên mọi channel; oversell-guard ở reservation ledger Sprint-1-redux. Lý do: leverage primitive đã có; tránh build velocity-engine; demo flash-sale-protection vẫn rõ.
- **Round-trip thật qua Shopee mock**: token bucket + breaker chỉ có ý nghĩa khi có downstream thật. Mock đã có sẵn từ Sprint-4 U7, không thêm carrying cost.
- **Per-SKU `is_flash_sale` flag từ config**: chọn vì đơn giản, demo rõ, không cần state-tracking burst rate; phù hợp portfolio scope.
- **Module mới `ShopFlow.StockSync`**: đúng cadence 6-service modular monolith; W6 mechanical split sẽ dễ; tránh cycle Inventory ↔ Channel.
- **Stock change source = Inventory outbox events** (Inferred, user confirmed): tận dụng events Sprint-3-redux đã ship; không cần polling DB; idempotency theo `event_id` của outbox.

---

## Dependencies / Assumptions

- Shopee mock server (Sprint-4 U7) tiếp tục chạy như Aspire resource trong dev; chaos endpoints (`__chaos`) đã có.
- `IChannelAdapter.PushStockUpdate` của Shopee adapter Sprint-4 đã có stub method; Sprint-5 hoàn thiện implementation thay vì tạo interface mới.
- `IProductMappingService` (Sprint-4 U6) đã expose SKU mapping; flag `is_flash_sale` có thể attach vào `ProductMapping` aggregate hoặc tách bảng riêng — quyết định ở plan.
- Reservation ledger Sprint-1-redux đã được Sprint-3-redux mở rộng với `TryReserveLinesAsync` + cấu trúc atomic; StockSync chỉ **đọc** state, không sửa.
- K12 per-tenant DbContext binding pattern + `OutboxRouteRegistry` Sprint-4 U4 hoạt động đúng dưới tải burst (giả định đã verified ở scale gate Sprint-3-redux).
- `FairnessCalculator` của tests/Common (Sprint-1-redux + Sprint-4.5) tái dùng được cho R8.
- Docker daemon vẫn vắng trên dev machine; scale gate đo trên CI nightly — chấp nhận precedent Sprint-1-redux..4.5.

---

## Outstanding Questions

### Resolve Before Planning

- *(không có — toàn bộ product decisions đã chốt qua dialogue)*

### Deferred to Planning

- [Affects R10][Technical] `is_flash_sale` flag đặt ở `ProductMapping` của Channel module (đã có) hay tách bảng riêng `stock_sync_sku_flag` của StockSync? Trade-off coupling vs locality.
- [Affects R3, R4][Technical] Implementation của coalescing buffer: in-memory `ConcurrentDictionary<(TenantId, Sku, Channel), Entry>` + `PeriodicTimer` flush, hay dùng `System.Threading.Channels`? Plan quyết.
- [Affects R6][Technical] Token bucket: tự cài bằng `SemaphoreSlim` + refill timer, hay dùng thư viện sẵn (`System.Threading.RateLimiting`)? Plan quyết.
- [Affects R7][Technical] Polly v8 pipeline shape: tái dùng pipeline của Sprint-4 ShopeeAdapter hay tạo pipeline mới với strategy CircuitBreaker stand-alone? Plan quyết.
- [Affects R2][Needs research] Inventory đang expose snapshot read như thế nào (port nào tính được `available_to_sell` tại thời điểm gọi)? Có cần mở rộng `IReservationRepository` thêm read API không? Plan đọc code.
- [Affects R11][Technical] Shopee adapter `PushStockUpdate` chữ ký + body shape theo Shopee Open Platform v2 — plan tra cứu fixture của Sprint-4.

### Roadmap còn lại (high-level — brainstorm riêng sau Sprint-5)

- **Sprint-6 (Phase-2 W9-W10) Analytics module** — read-side projections (CQRS) cho dashboard: stock turnover, fulfillment SLA, top SKU per channel. Consume tất cả domain events từ outbox; ghi vào read-DB riêng per tenant. Demo dashboard nhỏ.
- **Phase-3 polish (W11-W12)** — Gateway YARP hardening (auth middleware, rate-limit ingress, request-ID propagation), observability stack (Prometheus + Grafana dashboards cho 4 hot metrics: reservation latency, push latency, breaker state, fairness floor), Lazada/TikTok adapter skeleton, deployment docs (Docker Compose prod compose file + runbook), portfolio README + demo video (5 phút walkthrough).

