# ShopFlow WMS — Canon Supersession History

This file records architectural decisions that change the foundational shape of the system. ADRs land in `docs/adr/`; this file is the thin index pointing at them with the date and trigger context. Implementation-level changes live in commits and `docs/solutions/`, not here.

---

## 2026-05-11 — Multi-tenancy pivot: RLS-shared → Database-per-tenant

**Trigger**: Phase-1 Sprint-1 integration test run on Docker host surfaced three findings within one hour:

1. Hand-authored EF migration silent no-op (missing `[Migration]` + `[DbContext]` attributes) — captured in [docs/solutions/2026-05-10-ef-migration-needs-attributes.md](solutions/2026-05-10-ef-migration-needs-attributes.md).
2. SERIALIZABLE 40001 race on conditional CTE INSERT — repository code did not catch; W3 scale gate's premise broke.
3. User compliance lens: PDPA SEA hard isolation requires physical tenant separation; RLS is a logical guarantee, weaker under audit scrutiny than DB-per-tenant.

**Decision**: [ADR-0003](adr/0003-database-per-tenant-for-compliance.md) — database-per-tenant on shared Postgres cluster. Compliance anchor: **PDPA Vietnam + Singapore PDPA**. Scale anchor: **25-50 validated tenants on single cluster**. Routing: per-request via middleware. PgBouncer in transaction-pooling mode is non-optional infrastructure.

**Supersedes**:
- v2.0 of `01-product-development-plan.md.docx` and `02-technical-design-document.md.docx` (the canon assumed RLS-from-day-1 single-tenant MVP)
- ADR-0001 + ADR-0002 carry postscripts noting the "RLS-as-cheapest-decision" claim is superseded; the ADRs themselves stand
- AGENTS.md §3 rewritten (7 RLS rules → 7 routing-and-catalog rules)
- `docs/plans/2026-04-27-001-feat-shopflow-wms-phase-0-bootstrap-plan.md` (Phase-0 plan v2.0) — superseded by [Phase-0-redux plan](plans/2026-05-11-002-phase-0-redux-bootstrap-plan.md)
- `docs/plans/2026-05-10-001-feat-inventory-reservation-ledger-impl-plan.md` (Sprint-1 plan v2.0) — superseded by [Sprint-1-redux plan](plans/2026-05-11-003-phase-1-sprint-1-redux-reservation-ledger-plan.md)

**New canon**:
- [docs/redesign/01-product-development-plan.md](redesign/01-product-development-plan.md) v3.0
- [docs/redesign/02-technical-design-document.md](redesign/02-technical-design-document.md) v3.0
- [ADR-0003](adr/0003-database-per-tenant-for-compliance.md)
- [docs/plans/2026-05-11-001-redesign-multi-tenancy-db-per-tenant-plan.md](plans/2026-05-11-001-redesign-multi-tenancy-db-per-tenant-plan.md) — the plan-of-plans

**Archive references**:
- Branch: `archive/phase-1-sprint-1-rls-shared` (was `feat/phase-1-sprint-1`)
- Tag: `archive/v0.1.0-phase-0-rls-shared` (annotated supersession note attached to the original `v0.1.0-phase-0` commit)

**Implementation branch** (active): `feat/phase-0-redux-db-per-tenant`

**Cost of pivot**: ~2 weeks of Phase-0 work + 1 week of Sprint-1 work-in-progress thrown away. Three learnings preserved (EF migration attributes, FsCheck Replay gamma format, green-against-stub property pattern). Trigger-to-decision elapsed time: ~1 hour. Decision-to-canon-committed elapsed time: ~half a day.

---

## 2026-05-12 — Phase-0-redux complete

**Tag**: [`v0.2.0-phase-0-redux`](https://github.com/longuit2002-blip/shopflow-wms/releases/tag/v0.2.0-phase-0-redux). Closes [Phase-0-redux plan](plans/2026-05-11-002-phase-0-redux-bootstrap-plan.md) U1-U10 on branch `feat/phase-0-redux-db-per-tenant`. Sign-off doc: [docs/phase-gates/2026-05-12-phase-0-redux-signoff.md](phase-gates/2026-05-12-phase-0-redux-signoff.md).

**Shipped**:
- DB-per-tenant foundation per [ADR-0003](adr/0003-database-per-tenant-for-compliance.md): SharedKernel (`IRequestContext`, `IDbContextFactory<T>`, `ITenantCatalog`, `OutboxDispatcher`, `TenantRoutingMiddleware`), ControlPlane catalog with mandatory-attribute migration, `shopflow-migrate` per-tenant runner CLI.
- Aspire AppHost wiring Postgres + PgBouncer (transaction pooling) + Redis + RabbitMQ + observability stack (Seq, Tempo, otel-collector, Prometheus, MinIO); chained bootstrap provisions `shopflow_control` + dev1 + dev2 before any service starts. Production handoff in `infrastructure/docker-compose.yml`.
- Inventory module (schema-only blessed reference) with the reservation-ledger schema locked: `UNIQUE(order_id)` idempotency anchor, `xid` row_version, no `tenant_id` on business tables. Repository methods throw `NotImplementedException("Sprint-1-redux …")` — the W1 green-against-stub state.
- 4 module shape replicas (Inbound/Outbound/Channel quartets, Analytics triplet) + Gateway YARP scaffold; per-module AGENTS.md ≤ 50 lines each.
- 4 ShopFlow Roslyn analyzers locked at Error: no raw DbSet, no `IPublishEndpoint.Publish` mid-transaction, no DbContext instantiation outside factory, no `DateTime.Now`.
- CI workflows: per-PR (build + csharpier + unit + Testcontainers migration smoke + cross-tenant routing); nightly chaos (integration + property + load + chaos placeholders).
- Operational `shopflow-gate phase-0-redux` CLI: catalog reachable, catalog migrated, all tenants Ready, PgBouncer reachable.

**Carried forward as canon**: docs/solutions/2026-05-10-ef-migration-needs-attributes.md (codified into `MigrationSmokeTests`).

**Deferred** (documented in sign-off): Aspire cold-start measurement and provisioning latency p99 (need Docker on the dev machine); CSharpier formatting cleanup of 23 files inherited from U4-U6; Inventory repository behavior (Sprint-1-redux); channel adapters + mock servers (Phase-2 Sprint-4); PgBouncer HA pair (Phase-2); tenant onboarding UI (Phase-3).

**Next**: [Sprint-1-redux reservation ledger plan](plans/2026-05-11-003-phase-1-sprint-1-redux-reservation-ledger-plan.md) cuts from this tag.

---

## 2026-05-12 — Phase-1 Sprint-1-redux complete

**Tag**: `v0.3.0-sprint-1-redux`. Closes [Sprint-1-redux plan](plans/2026-05-11-003-phase-1-sprint-1-redux-reservation-ledger-plan.md) U1-U6 on branch `feat/phase-1-sprint-1-redux-reservation-ledger`. Sign-off doc: [docs/phase-gates/2026-05-12-sprint-1-redux-signoff.md](phase-gates/2026-05-12-sprint-1-redux-signoff.md).

**Shipped**:
- `ReservationRepository` hot path: `TryReserveAsync` ships the conditional-CTE INSERT pattern at READ COMMITTED — the UPDATE on `stock_items` serialises contention via the row lock; the INSERT into `reservations_ledger` is gated on the UPDATE producing a row. Idempotency layered: app-level short-circuit via `FindByOrderIdAsync` + DB-level `UNIQUE(order_id)` with `23505` catch-and-refetch.
- Full `IReservationRepository` surface: `FindByOrderIdAsync`, `ConfirmAsync` (with NOT_FOUND / ALREADY_CONFIRMED / INVALID_STATE codes), `ReleaseAsync`, `ReleaseExpiredAsync` (multi-CTE batched UPDATE + outbox-per-row). Domain methods on `Reservation` and `StockItem` filled in for the same state machines on non-hot paths.
- Multiplexed `ReservationExpiryWorker` — one BackgroundService visits every `Ready` tenant per tick; per-tenant scope binds `RequestContext` before resolving the repository; per-tenant exception isolation keeps healthy tenants progressing.
- `ShopFlow.Inventory.IntegrationTests` (14 tests) — `ReservationRepositoryTests` covering happy path, exact-available, oversold, idempotency, concurrent oversell, FindByOrderId, Confirm, Release, ReleaseExpired; `ReservationExpiryWorkerTests` covering construction validation, single-tenant tick, multi-tenant fan-out, broken-tenant isolation; `MultiTenantScaleGateTests` (the W3 5×1000 fairness floor gate).
- `ShopFlow.PropertyTests` — 5 FsCheck properties on the reservation ledger (`HappyPathConcurrency_AllSucceed`, `StrictCapacity_NoOversell`, `Idempotency_OneUniqueId`, `ExpiryReleasesActiveRows`, `InvariantHoldsForAnyOperationSequence`) wired to a real `ReservationRepository` via the `ReservationRepositoryHandle` static-slot pattern.
- `InventoryOptions` config surface for the expiry worker (`ExpiryPollIntervalSeconds`, `ExpiryBatchSize`, `DefaultReservationTtlMinutes`).

**New compounding learning**: [docs/solutions/2026-05-12-readcommitted-conditional-cte-correctness.md](solutions/2026-05-12-readcommitted-conditional-cte-correctness.md) — captures the SERIALIZABLE→ReadCommitted decision rationale so the next conditional-write surface doesn't re-derive.

**Deferred** (documented in sign-off): Docker-backed measurement of W3 scale-gate p99 and fairness floor (Docker daemon not running this session); `GetActiveSumAsync` / `GetConfirmedSumAsync` read-back surface (Sprint-2-redux); multi-instance expiry worker leader election (Phase-2); `StockItemRepository` behavior (Sprint-2-redux for Inbound's GRN flow); NBomber promotion of the load harness; CSharpier formatting cleanup (carried).

**Next**: Sprint-2-redux (Inbound module W4) cuts from `v0.3.0-sprint-1-redux`.

---

## 2026-05-13 — Phase-1 Sprint-2-redux complete

**Tag**: `v0.4.0-sprint-2-redux`. Closes [Sprint-2-redux plan](plans/2026-05-13-001-feat-phase-1-sprint-2-redux-inbound-plan.md) U1-U10 on branch `feat/phase-1-sprint-2-redux-inbound`. Sign-off: [docs/phase-gates/2026-05-13-sprint-2-redux-signoff.md](phase-gates/2026-05-13-sprint-2-redux-signoff.md).

**Shipped**:
- Inbound module quartet: 5 domain entities (PurchaseOrder + Line, Receiving + Line, ReconciliationTicket) with full state machines, repository surface, `ConfirmReceivingLineService` orchestrator, 6-table initial migration with hand-authored attributes + `UNIQUE(receiving_id, purchase_order_line_id)` idempotency anchor.
- Inventory schema extension: 4 new tables (zones, bins, stock_item_bins composite-PK, inbound_dedup composite-PK) + nullable `home_zone_id` FK on stock_items. Bin-aware `StockItemRepository.AdjustAtBinAsync` (UPSERT stock_items + stock_item_bins, UPDATE available + bin occupancy, INSERT audit row — all atomic in ReadCommitted transaction). `PutAwaySuggestionService` ranks top-K bin candidates by `(zone_priority, available_capacity DESC, occupancy ASC, bin name lex)`.
- First cross-module integration event: `ShopFlow.Contracts.Inbound.InboundConfirmedV1`. `IInboundOutbox` explicit-write port (Sprint-1-redux's `AppendOutbox` pattern). `InboundConfirmedConsumer` in Inventory idempotent via `inbound_dedup` INSERT-then-catch-23505. Header-vs-payload tenant-id mismatch rejection as defense-in-depth.
- MassTransit transport flip W6 → W4: `ShopFlowDefaultsOptions.MessageBusTransport` (`InMemory` | `RabbitMq`, default `RabbitMq`) + config override at key `MessageBus:Transport`. RabbitMQ connection from `configuration.GetConnectionString("rabbitmq")` (Aspire-injected). ADR-0002 postscript dated 2026-05-13.
- `AddShopFlowDefaults` wired in Inbound.Api + Inventory.Api Program.cs (closes the Phase-0-redux U10 deferral). Standard middleware order: UseProblemDetails → UseTenantRouting → MapControllers.
- HTTP surface: `PurchaseOrdersController` thin endpoints (POST/GET PO, PATCH /open + /cancel, POST /receive). Thin controllers calling services directly; MediatR layer can land on top without rework.
- Tests: 110 unit + 52 integration green. New: 19 Inbound.UnitTests, 10 Inbound.IntegrationTests, 11 new Inventory.IntegrationTests (4 AdjustAtBin + 4 PutAwaySuggestion + 3 InboundConfirmedConsumer).

**New compounding learning**: [docs/solutions/2026-05-13-cross-module-outbox-table-name-collision.md](solutions/2026-05-13-cross-module-outbox-table-name-collision.md) — both modules' migrations create `outbox_messages` in `public` schema; collision surfaces when a single tenant DB hosts both. Sprint-2.5 candidate (per-module table-name prefix `inbound_outbox_messages` / `inventory_outbox_messages` — touches Sprint-1-redux's existing references).

**Deferred** (documented in sign-off):
- U9 single-tenant-DB cross-module flow test — blocked by the outbox name collision. JSON-serialization-and-consume seam covered by U6 TestHarness tests + Sprint-1-redux's dispatcher loop validation.
- Real RabbitMQ runtime exercise (Testcontainers RabbitMQ + dispatcher poll-loop end-to-end). Transport switch code-complete; first failure modes surface in nightly CI on Linux.
- Reconciliation ticket resolution workflow (Phase-2 admin surfaces).
- Aspire AppHost registration of Inbound.Api + Inventory.Api as resources (so `WithReference(rabbitmq)` injection becomes load-bearing).
- MediatR command/handler wrappers (thin layer can land any time).
- `StockItemRepository.FindBySkuAsync` + non-bin `AdjustAsync` (Sprint-3-redux for Outbound picking).

**Next**: Sprint-2.5 (outbox table-name rename) or Sprint-3-redux (Outbound + saga) cuts from `v0.4.0-sprint-2-redux`.

---

## 2026-05-13 — Phase-1 Sprint-2.5 complete

**Tag**: `v0.4.1-sprint-2.5`. Closes the Sprint-2-redux U9 deferral. Branch `feat/phase-1-sprint-2.5-outbox-rename` cut from `v0.4.0-sprint-2-redux`. Sign-off: [docs/phase-gates/2026-05-13-sprint-2.5-signoff.md](phase-gates/2026-05-13-sprint-2.5-signoff.md).

**Shipped**:
- Per-module outbox table-name prefix: `inbound_outbox_messages` + `inventory_outbox_messages` (replaces shared `outbox_messages`). EF entity configs + migrations updated; Phase-0-redux U8 Inventory migration edited in-place (no production data yet).
- 2 cross-module flow integration tests in `tests/ShopFlow.Inbound.IntegrationTests/InboundToInventoryFlowTests.cs` exercising the full Inbound → outbox → MassTransit publish → InboundConfirmedConsumer → Inventory stock pipeline against a single shared Testcontainers Postgres DB. The U9 deferred-from-Sprint-2-redux gap is now closed.
- `ShopFlow.SharedKernel.Infrastructure.OutboxJsonOptions.Default`: single source of truth for outbox JSON serialization (CamelCase + case-insensitive deserialize). Consolidated 4 private options (OutboxInterceptor, MultiplexedOutboxDispatcher, InboundOutbox, ReservationRepository). Fix for a latent Sprint-1-redux ship that didn't surface until a real consumer round-tripped a serialized payload.
- Tests: 110 unit (unchanged) + 54 integration (+2 cross-module flow).

**Resolved learnings**: [docs/solutions/2026-05-13-cross-module-outbox-table-name-collision.md](solutions/2026-05-13-cross-module-outbox-table-name-collision.md) marked resolved with backreference to the sign-off.

**Carry-forward rules**:
- Hand-authored module migrations may be edited in-place ONLY before any production tenant has been provisioned against them. After production application, the rename / structural changes require a separate timestamped migration.
- All outbox JSON serialize / deserialize must go through `OutboxJsonOptions.Default`. Private options on the writer side without matching options on the reader side = silent payload corruption.

**Next**: Sprint-3-redux (Outbound + fulfillment saga, W5) cuts from `v0.4.1-sprint-2.5`.

---

## 2026-05-13 — Phase-1 Sprint-3-redux complete

**Tag**: `v0.5.0-sprint-3-redux`. Closes Phase-1's customer funnel — Inventory holds stock (Sprint-1-redux), Inbound fills it (Sprint-2-redux), Outbound drains it (Sprint-3-redux). Branch `feat/phase-1-sprint-3-redux-outbound` cut from `v0.4.1-sprint-2.5`. Sign-off: [docs/phase-gates/2026-05-13-sprint-3-redux-signoff.md](phase-gates/2026-05-13-sprint-3-redux-signoff.md).

**Shipped**:
- **Outbound module quartet**: Domain/Application/Infrastructure/Api with `Order` + `OrderLine` + `PickWave` + `PickAssignment` + `Picker` aggregates, `IOrderRepository` + `IUnitOfWork` + `IOutboundOutbox` + `IPickQueue` + `IPickWaveRepository` + `IPickerRepository` + `IMockShippingProvider` ports, idempotent `POST /api/outbound/orders` + `GET /api/outbound/orders/{id}` + 4 saga-driving endpoints (`confirm-pick` / `confirm-pack` / `confirm-ship` / `mark-pick-failed`).
- **`FulfillmentSaga` MassTransit state machine**: 11 states (Initial → AwaitingReservation → Reserved → AwaitingPick → Picked → Packed → AwaitingShip → Shipped terminal / CompensatingReservation → Cancelled terminal); EF saga repository against `saga_state` table; **K12 per-tenant DbContext binding** via `TenantBindingSagaFilter<T>` — saga's writes always land in the right tenant DB (`SagaPerTenantBindingTests` proves zero cross-contamination across two provisioned tenants).
- **K15 verified**: `MassTransit.EntityFrameworkCore` 8.3.4 + EF Core 9 bind cleanly; no Redis saga-repo fallback needed.
- **Inventory schema extension**: `reservations_ledger` gains `order_line_id text NOT NULL` column; UNIQUE moves from `(order_id)` to `(order_id, order_line_id)` — supports multi-line orders atomically. Sprint-1-redux single-line callers use sentinel `'_default'` (backwards-compat).
- **`IReservationRepository` multi-line ports**: `TryReserveLinesAsync` (atomic multi-row CTE; all-or-nothing across N lines per order) + `ReleaseLinesAsync` (partial-set release for saga compensation). Existing `TryReserveAsync` becomes a backwards-compat wrapper. `ConfirmAsync`/`ReleaseAsync` SQL rewritten to per-sku aggregation for same-sku-multi-line correctness.
- **9 cross-module contracts**: `ShopFlow.Contracts.{Outbound,Inventory}.*` — `OrderPlacedV1`, `TrackingPushedV1`, `ReserveStockV1`, `ConfirmStockV1`, `ReleaseStockV1`, `StockReservedV1`, `StockReservationFailedV1`, `StockConfirmedV1`, `StockReleasedV1`.
- **3 Inventory consumers** (`ReserveStockConsumer`, `ConfirmStockConsumer`, `ReleaseStockConsumer`) wrap the extended `ReservationRepository`; auto-registered via `AddConsumers(asm)` in `AddShopFlowDefaults`'s assembly scan; emit result events via `inventory_outbox_messages`.
- **`IPickQueue` + `PickWaveGeneratorService`**: per-tenant `ConcurrentDictionary<Guid, Channel<PickRequestV1>>` with bounded capacity 1000; `PeriodicTimer(30s)` BackgroundService drains channels, batches by `(tenant_id, shipping_profile)`, emits `PickWave` rows when window ages past 15 min OR `max_wave_size=50`; round-robin picker assignment via deterministic cursor.
- **Mocked shipping carrier**: `MockShippingProvider` with 1-3s configurable delay + Polly v8 `ResiliencePipelineBuilder` retry (3 retries × 200ms constant backoff) + 5% transient-fail injection; `ChannelTrackingConsumer` stub for `TrackingPushedV1` (moves to Channel module in Phase-2 Sprint-4).
- **Saga compensation path**: Set-based dedup on `StockReleasedV1` via `ReleasedLineSkus` HashSet on saga state — counter `LinesAwaitingRelease` decrements only on first sight per line; protects against MassTransit at-least-once redelivery driving the counter negative. `OrderCancelledConsumer` propagates saga's terminal Cancelled state to the Order row (R3 eventual-consistency).
- **W5 scale gate** (`Category=Load`): `MultiTenantOutboundScaleGateTests` — 2 tests, 2000 orders × 3 tenants. Operator-pipeline path (saga bypassed — documented limitation; see sign-off). Dev-laptop measurements: Shipped p99 247-332ms/tenant; Cancelled p99 112-131ms/tenant; fairness floor 0.918-0.979 (Shipped) and 0.861-0.898 (Cancelled-variant). Production CI re-validates nightly.
- **4 per-PR integration test classes**: `SagaHappyPathTests` (full saga happy path + idempotent duplicate POST), `SagaCompensationFlowTests` (Path A empty-set + Path B pick-failure), `CrossModuleReservationFlowTests` (Outbound + Inventory on one DB — real `ReserveStockConsumer` + `ConfirmStockConsumer` against real ledger), `PickWaveBatchingFlowTests` (50 orders → 2 waves by shipping_profile, round-robin picker). 7 tests in ~3s when sharing the test collection container.
- Tests: ~270 non-load unit + ~120 integration + 4 load = ~390 tests at sprint close.

**New `docs/solutions/` entries**:
- [docs/solutions/2026-05-13-multi-row-cte-predicate-must-live-in-update.md](solutions/2026-05-13-multi-row-cte-predicate-must-live-in-update.md) — caught by Sprint-1-redux's existing concurrent-oversell test when U3 first attempted the K11 pseudocode. The plan's pre-check `will_succeed` CTE was unsafe under READ COMMITTED concurrency; corrected pattern moves the predicate INSIDE the UPDATE.

**Carry-forward rules**:
- Any conditional CTE under READ COMMITTED that gates a state transition must embed the predicate inside the UPDATE WHERE clause. Pre-check CTEs (factored for readability) break the snapshot guarantee under concurrency.
- Multi-row extensions of single-row patterns need fresh concurrency review — the single-line shape's correctness doesn't extend automatically.
- The K13 `OutboxDispatcher.Publish`-for-commands shape is accepted for Sprint-3-redux's modular-monolith stance. Phase-2 W6 mechanical split MUST add envelope-type → endpoint routing in `OutboxDispatcher` before deploying commands across process boundaries.

**Documented limitation**: U8's W5 scale gate bypasses the saga (operator-pipeline measurement only). Saga correctness validated at unit + integration scale by U4/U7/U9 tests; full-saga-throughput-under-load is a Phase-2 production-CI measurement gap.

**Next**: Phase-2 Sprint-4 (Channel Connections + webhook idempotency) cuts from `v0.5.0-sprint-3-redux`. K13's envelope-type → endpoint routing in `OutboxDispatcher` is a Phase-2 prerequisite for the W6 mechanical split.

---

## 2026-05-13 — Phase-2 Sprint-4 complete

**Tag**: `v0.6.0-sprint-4`. Opens Phase-2's channel-ingress half. Branch `feat/phase-2-sprint-4-channel-webhook` cut from `v0.5.0-sprint-3-redux`. Sign-off: [docs/phase-gates/2026-05-13-sprint-4-signoff.md](phase-gates/2026-05-13-sprint-4-signoff.md).

**Shipped**:
- **Channel module Domain**: `Channel` aggregate (Active|Disabled lifecycle, idempotent Disable), `WebhookEvent` aggregate (Received → Processed|Failed, signature_verified flag), `ProductMapping` aggregate (Exact|Fuzzy|Manual method discipline). Value objects `ProviderEventId` (case-sensitive, max 200) and `ExternalSku` (case-insensitive ordinal — marketplaces case-fold).
- **Channel module DbContext + InitialChannelSchema migration**: 4 tables (`channels`, `webhook_events`, `product_mappings`, `channel_outbox_messages`). `UNIQUE(channel_id, provider_event_id)` on webhook_events is the Sprint-4 R3 idempotency anchor; `UNIQUE(channel_id, external_sku)` on product_mappings; check constraint `mapping_method IN ('Exact','Fuzzy','Manual')`; per-module outbox prefix per Sprint-2.5. 5th method added to `MigrationSmokeTests`.
- **Webhook receiver pipeline (R3/R4/R5)**: `ISignatureVerifier` + `ShopeeSignatureVerifier` (HMAC-SHA256 + `CryptographicOperations.FixedTimeEquals` constant-time compare, bad-input → false), `SignatureVerifierFactory` keyed by channel type, `IWebhookEventRepository` + `WebhookEventRepository` (BEGIN tx ReadCommitted → INSERT → catch `PostgresErrorCodes.UniqueViolation` (23505) → rollback + SELECT existing → return `IsDuplicate=true` per Sprint-1-redux pattern), `IChannelOutbox` + `ChannelOutbox` (mirrors `OutboundOutbox`), `IngestWebhookService` orchestrator (first-write only appends outbox row + SaveChanges atomic), `[SkipTenantRouting]` attribute + `TenantRoutingMiddleware` opt-out (endpoint-metadata check), `WebhooksController` (POST `/api/channel/webhooks/{channelType}/{channelId}` — 404 unknown channel / 401 HMAC mismatch / 501 unsupported channel type / 200 with `{eventId, isDuplicate}`).
- **K13 close — `IOutboxRouteRegistry`**: `SendKind` (Publish | Send), `OutboxRoute` (Kind, Exchange?, RoutingKey?) + `OutboxRoute.PublishDefault` static, `OutboxRouteRegistry` (ConcurrentDictionary + dual constructors: default + DI-seed enumeration with last-write-wins), `OutboxRouteSeed` record, `services.AddOutboxRoute<T>(SendKind, destination?)` extension. `MultiplexedOutboxDispatcher<TContext>.DispatchOneTenantAsync` reads the registry per row — Send kind routes through `ISendEndpointProvider.GetSendEndpoint(...).Send` (default destination = kebab-case CLR type name); Publish kind preserves Sprint-1/2/3 behaviour. Tenant + correlation headers stamped on both paths. **Unregistered types resolve to `OutboxRoute.PublishDefault` — zero changes to existing Sprint-1/2/3 paths.**
- **Adapter framework (R1)**: `IChannelAdapter` (ParseWebhook + PushStockUpdateAsync — Sprint-5 stub), `IChannelAdapterFactory` (case-insensitive `ResolveFor` + `TryResolve`), `ChannelAdapterFactory`, `ShopeeAdapter` (stateless; injects Polly v8 `ResiliencePipeline` + typed HttpClient for Sprint-5 stock-push wire-up), `ShopeeWebhookParser` (Shopee envelope shape: `event_id`, `event_type`, `shop_id`, `timestamp`, `data`; forward-compat unknown fields), `UnknownChannelTypeException` (loud on Sprint-6+ rollout misconfigurations).
- **Product mapping engine (R6)**: `IProductMappingRepository` + `IProductMappingService`, `ProductMappingRepository` (UNIQUE-23505 idempotency for admin manual upsert), `HybridProductMappingService` (Exact DB lookup → in-process Levenshtein @ threshold 0.6 → null; iterative two-row Levenshtein bounded at O(min |a|, |b|) allocation), `ProductMappingsController` (POST manual + POST resolve + GET paged list per channel).
- **Shopee mock server**: separate-process Kestrel-hosted ASP.NET project at `tools/mocks/shopee/`. HMAC-SHA256 outgoing signing via independent `ShopeeSigner` (intentionally duplicates the verifier algorithm so mock/prod drift surfaces during tests). In-memory `SecretRegistry` (seeded from appsettings `Channels` array; `POST /__seed-channel` adds runtime entries). `ChaosState` singleton with `POST /__chaos` toggle (Rate429 / Rate500 / LatencyJitterMs). `POST /__send-webhook` test driver builds Shopee envelope, signs with channel secret, POSTs to receiver with advertised rate-limit headers. Aspire AppHost wires via `AddProject<Projects.ShopFlow_Mocks_Shopee>("shopee-mock")` — replaces the U9 placeholder comment.
- **Channel→Outbound bridge (R7/R8)**: `ShopFlow.Contracts.Channel.OrderImportedV1` + `OrderImportedLineV1` records. `OrderImportedConsumer` in Outbound.Application — idempotent on `Order.ChannelExternalOrderId` UNIQUE (Sprint-3 U2), reuses existing `IOrderRepository` + `IUnitOfWork` + `IOutboundOutbox` ports (no self-HTTP loopback), enqueues canonical `OrderPlacedV1` outbox row so the Sprint-3 saga starts from its existing entry point. Channel module registers `services.AddOutboxRoute<OrderImportedV1>(SendKind.Send)`; Outbound's existing `AddConsumers(asm)` discovers the consumer automatically.
- **Channel.Api Program.cs**: composition order `AddShopFlowDefaults → AddControlPlane → AddChannelModule → UseProblemDetails → UseTenantRouting → MapControllers` per AGENTS.md §11.79. appsettings dev defaults for ConnectionStrings:rabbitmq, MessageBus:Transport=RabbitMq, ControlPlane:ConnectionString + TenantTemplate, Channel:Shopee:MockBaseUrl.
- Tests: **269 non-load unit tests** (+30 from Sprint-4): 32 Channel Domain + 9 SigVerifier + 3 SigVerifierFactory + 6 IngestService + 7 ShopeeParser + 3 AdapterFactory + 8 HybridMapping + 1 TimeRoundtrip + 8 OutboxRouteRegistry + 3 OrderImportedConsumer (MT TestHarness + NSubstitute). Channel.IntegrationTests project skeleton + `MultiTenantWebhookScaleGateTests` with 3 `Skip`'d `Category=Load` slots (harness body is a documented follow-up).
- **47 projects total**: 33 src (adds `ShopFlow.Mocks.Shopee`) + 14 test (adds `ShopFlow.Channel.IntegrationTests`).

**Documented limitations / Sprint-4.5 follow-ups**:
- **`WebhooksController` parser wire-up pending** — U3 currently derives the idempotency token from a hash of `(body, signature)`; the U5 `ShopeeWebhookParser` is registered in DI but the controller still uses the stub. A follow-up commit calls `IChannelAdapterFactory.TryResolve(...)?.ParseWebhook(...)` and passes the parsed `WebhookEnvelope.ProviderEventId`. UNIQUE constraint catches replay either way.
- **`OrderImportedV1` not yet emitted by the receiver** — U8 ships the contract type + Outbound consumer + K13 Send-route registration, but the receiver writes a placeholder `"ShopFlow.Channel.Webhooks.WebhookReceivedV1"` event type. Same follow-up commit wires the receiver to resolve SKUs via `IProductMappingService` + build + emit `OrderImportedV1`.
- **Scale-gate harness body deferred** — `TenantWebhookHarness`, multi-tenant Testcontainers provisioning, `WebApplicationFactory`-hosted Channel.Api setup, and burst-driver coordination land in a follow-up. The class skeleton + 3 `Fact(Skip=…)` slots are in place; the shape is identical to Sprint-1-redux + Sprint-3-redux scale gates which are the templates.
- **No real Postgres / integration / runtime smoke on this dev machine** — Docker daemon not running (same Sprint-1-redux + Sprint-3-redux posture). All Category=Integration tests including the new 5th `MigrationSmokeTests.ChannelMigration_AppliesAndLeavesNamedObjects` method are deferred to CI.
- **No real RabbitMQ broker round-trip for the new K13 Send path** — in-memory MT covers correctness; production broker behaviour deferred to Phase-2 production CI.

**Carry-forward rules**:
- `IOutboxRouteRegistry` resolution defaults to Publish — adding a new event/command type is publish-by-default; opting into Send requires `services.AddOutboxRoute<T>(SendKind.Send)` at composition time. Last-write-wins across module composition.
- HMAC verification must use `CryptographicOperations.FixedTimeEquals` for constant-time comparison. Regular byte/string equals on signature comparison is a timing-attack hole.
- Marketplace adapters live in `Channel.Infrastructure.Adapters` (NOT Application) — adapters are infrastructure; Application's job is to receive ingress and dispatch.
- Marketplace mocks live as **separate processes** (not in-process) at `tools/mocks/{shopee,lazada,...}`. Channel AGENTS.md §11.6. The Sprint-3 `IMockShippingProvider` in-process pattern is for adapter unit tests, not receiver integration.
- Adding a new marketplace adapter is one DI registration line in `AddChannelModule` + one adapter file. Sprint-6's Lazada proves this contract.

**Next**: Sprint-5 (Stock Sync Engine — coalescing buffer per `(tenant, sku, channel)`, per-channel token bucket per tenant, priority queue for flash-sale SKUs, Polly circuit breaker per `(tenant, channel)`, allocation engine). The Sprint-5 scale gate is the headline noisy-neighbor test: 5 tenants concurrently, Tenant A bursts 2k stock changes/s for 5min, Tenants B-E maintain p99 < 30s, per-tenant fairness floor ≥ 0.85. Plan still to be written.

Parallel option: Sprint-4.5 follow-up commit landing the parser wire-up + harness body before opening Sprint-5.

---

## 2026-05-15 — Phase-2 Sprint-4.5 complete

**Tag**: `v0.6.1-sprint-4.5`. Closes the four Sprint-4 sign-off deferrals as a ~1-week point-release. Branch `feat/phase-2-sprint-4.5-webhook-followup` cut from `v0.6.0-sprint-4`. Sign-off: [docs/phase-gates/2026-05-15-sprint-4.5-signoff.md](phase-gates/2026-05-15-sprint-4.5-signoff.md).

**Shipped** (6 implementation units U1-U6 per [plan](plans/2026-05-14-001-feat-phase-2-sprint-4.5-webhook-followup-plan.md)):

- **U1 IChannelAdapter.ParseOrderCreated**: new interface method returning `Result<ExternalOrderDraft>` — Channel-internal shape carrying `(ChannelExternalOrderId, ShippingProfile, Lines[(ExternalSku, Qty)])`. `ShopeeAdapter` implementation gates on `EventType == "order.created"` and delegates to a new `ShopeeWebhookParser.ParseOrderData(rawPayload)` method that reads the real Shopee Open Platform v2 wire shape (`data.ordersn`, `data.items[].item_sku`, `data.items[].model_quantity_purchased`, `data.package_list[0].shipping_carrier`). 13 unit tests pinning happy paths (2-line, single-line, real fixture shape) + 7 failure paths with stable error codes (`shopee.order.{event_type_unsupported, ordersn_required, items_empty, line_sku_required, line_quantity_invalid, data_malformed, data_missing}`).
- **U2 WebhooksController parser wiring**: replaces `ExtractProviderEventIdStub` (body+signature hash) with `IChannelAdapterFactory.TryResolve(channelType).ParseWebhook(...)` — the receiver's idempotency token now derives from the marketplace-asserted `event_id`. Stub method deleted. Parse failures route to HTTP 400 with stable error code. Unknown channel type → 501. `BuildHeaderSnapshot()` helper produces case-insensitive header dict for the adapter parser.
- **U3 WebhookOrchestrator** (new `ShopFlow.Channel.Application.Webhooks.WebhookOrchestrator`): event-type gating + per-line `IProductMappingService.ResolveAsync` + **fail-whole-import on any unmapped line** per the `OrderImportedV1` contract canon. Only `EventType == "order.created"` emits a downstream row; other event types persist `webhook_events` (audit) and emit no actionable row. Unmapped lines mark the row with `status=Failed` via `WebhookEvent.MarkFailed(reason, now)`, skip the outbox, and surface a structured warning log carrying `(channel_id, ordersn, unmapped_skus[])`. All-mapped happy path builds `OrderImportedV1` with Channel-side-minted `OrderId`, tenant from `RequestContext`, per-line `InternalSku` from the resolution, and passes `typeof(OrderImportedV1).AssemblyQualifiedName!` as the outbox `event_type` (K13 routes via `SendKind.Send`). `IngestWebhookService.IngestFailedAsync` is the new failed-path entry point (status=Failed row + no outbox, UNIQUE-23505 idempotency preserved). Controller delegates to orchestrator; maps `WebhookProcessStatus` (OrderImported / ImportFailed / EventSkipped) to 200-shape responses. **Brainstorm R6 reversal documented** — the original brainstorm's emit-with-`InternalSku=null` was structurally impossible against the non-nullable `OrderImportedLineV1.Sku` contract; the canon's fail-whole-import policy is what U3 implements.
- **U4 TenantWebhookHarness**: integration test infrastructure under `tests/ShopFlow.Channel.IntegrationTests/Harness/`. `ChannelWebhookFixture` (xUnit collection fixture — one Testcontainers Postgres per assembly). `TenantWebhookHarness` (per-test orchestrator — provisions control DB + N tenant DBs, applies Channel migrations, registers tenants in catalog + channels with secrets, boots `WebApplicationFactory<Program>` with config overrides for Postgres + `MessageBus:Transport=InMemory`). `SendAsync(tenantIndex, eventType, ordersn, items, signWithTenantIndex?, eventId?)` signs + POSTs through the real controller pipeline. DB-count helpers (`CountWebhookEventsAsync`, `CountOutboxRowsAsync`, `GetOutboxRowsAsync`). `SeedManualMappingAsync` for happy-path mapping pre-seeding. `SignedWebhookSender` mirrors `ShopeeSigner` (deliberate duplication to surface drift). One smoke test (`Category=Integration`) — 2 tenants × 1 webhook each → 1 row each tenant, no cross-tenant contamination.
- **U5 MultiTenantWebhookScaleGateTests** (3 `Category=Load` bodies replacing the Sprint-4 `Skip` slots):
  - **Burst-200rps × 5 tenants × 5s** → per-tenant p99 < 200ms AND fairness floor ≥ 0.85 (min/max p99 across tenants via `FairnessCalculator`). Warm-up phase per Sprint-3-redux pattern. Sanity check on per-tenant `webhook_events` count = 1001 (warmup + burst).
  - **Replay-100× with fixed `event_id`** → exactly 1 `webhook_events` row + 1 `channel_outbox_messages` row. Forces same Shopee envelope `event_id` across all 100 sends via the new harness `eventId` parameter; UNIQUE-23505 catches 99 replays.
  - **Cross-tenant signature** → 401 + zero DB writes in either tenant (signature mismatch returns before tenant binding per Sprint-4 controller order).
- **U6 sign-off**: this CHANGELOG entry + sign-off doc at `docs/phase-gates/2026-05-15-sprint-4.5-signoff.md` + README/CLAUDE.md "Current stage" updates + plan frontmatter `status: completed` + annotated tag `v0.6.1-sprint-4.5`.
- Tests: **288 non-load unit tests** (+19 from Sprint-4.5): Channel 89 (+20) from 13 ShopeeAdapterParseOrderCreated + 7 WebhookOrchestrator unit tests. 1 new `Category=Integration` test (`TenantWebhookHarnessSmokeTests`). 3 `Category=Load` tests in tree (Skip removed).

**Documented limitations / carried-forward deferrals**:
- **Runtime smoke** deferred — Docker daemon not running on this dev machine (same Sprint-1/3/4 posture). CI runs the full integration + Load suite; first nightly post-Sprint-4.5 lands the actual p99 + fairness numbers.
- **Per-event-type policies beyond `order.created`** — Sprint-4.5 emits `OrderImportedV1` for `order.created` only; other event types persist via a sentinel `WebhookEventSkippedV1` event type the `OutboxRouteRegistry` treats as `PublishDefault` (no subscriber → no-op at broker). Sprint-6+ refines into explicit per-event-type emission.
- **Mapping batch resolution** — Sprint-4.5 ships per-line `IProductMappingService.ResolveAsync` (in-process Levenshtein); if U5 burst measurements surface this as a hotspot, a `ResolveBatchAsync` port is the fast-follow.
- **Multi-instance dispatcher leader election** — still a Phase-2 nice-to-have; not Sprint-4.5 scope.

**Carry-forward rules**:
- `IChannelAdapter` now has THREE responsibilities (envelope parse, order-shape extract, stock push) — Sprint-6 Lazada implements the same three methods against Lazada's order shape; one DI registration line + one adapter file.
- Receiver fail-whole-import on unmapped lines is canon per `OrderImportedLineV1.Sku` being non-nullable + the contract docs' explicit policy statement. Any change requires both a contract migration AND a Outbound consumer update.
- Harness pattern (`TenantWebhookHarness`) is the integration-test seam for Channel-side multi-tenant scenarios. Sprint-5's stock-push tests can extend the same shape (add an "outbound" mode that drives the sync engine's input side).

**Next**: Sprint-5 Stock Sync Engine (Phase-2 W6-W8 centerpiece per product plan v3.0 §9.4). Plan still to be written.

---

## 2026-05-17 — Phase-2 Sprint-5 complete

**Tag**: `v0.7.0-sprint-5`. Closes Phase-2 egress with a new `ShopFlow.StockSync` module (7th logical module) consuming Inventory's stock-mutation outbox stream and pushing per-channel `available_to_sell` updates through a four-layer isolation pipeline (coalescing buffer → per-tenant priority queue → token bucket → Polly v8 circuit breaker → `IChannelAdapter.PushStockUpdateAsync`). Ten implementation units shipped on `feat/phase-2-sprint-5-stock-sync` cut from `v0.6.1-sprint-4.5`. Sign-off: [docs/phase-gates/2026-05-17-sprint-5-signoff.md](phase-gates/2026-05-17-sprint-5-signoff.md).

**Shipped** (10 implementation units U1-U10 per [plan](plans/2026-05-16-001-feat-phase-2-sprint-5-stock-sync-plan.md)):

- **U1 Module quartet scaffold**: `ShopFlow.StockSync.{Domain,Application,Infrastructure,Api}` csproj quartet; `StockSyncDbContext` with 3 module-owned tables (`stock_sync_sku_flag`, `stock_sync_push_log` UNIQUE on `idempotency_key`, `stock_sync_outbox_messages` per Sprint-2.5 per-module prefix canon); `InitialStockSyncSchema` migration with mandatory `[Migration]` + `[DbContext]` attributes; `SkuFlag` (string PK) + `PushLogEntry` (BIGSERIAL Id, MarkSucceeded/Failed/BreakerOpen factories) aggregates; 14 unit tests + 6th smoke test method (`StockSyncMigration_AppliesAndLeavesNamedObjects`); 6 new csproj entries in `ShopFlow.sln`.
- **U2 StockLevelChangedV1 contract + Inventory emit (KTD1)**: new `ShopFlow.Contracts.Inventory.StockLevelChangedV1(TenantId, Sku, AvailableToSell, OccurredAt)` canonical event. `ReservationRepository` + `StockItemRepository` emit one row per affected SKU per commit across all 5 stock-mutating paths (TryReserve, Confirm, Release, ReleaseLines, ReleaseExpired) + AdjustAtBin. Generic `AppendOutbox<T>` overload + `ReadAvailableForSkusAsync` follow-up SELECT helper for CTE paths that don't surface new `available` in RETURNING. **KTD1 replaces literal R1** (consume 3 existing transition events) — `StockReleasedV1` only carries `OrderLineIds`, `StockConfirmedV1` has no per-line detail; coupling StockSync to Outbound for line→SKU mapping was race-prone. Inventory's existing `StockChangedEvent` domain event already had the right intent ("catch-all for the stock-sync engine"). 6 integration tests against Testcontainers Postgres.
- **U3 Coalescing buffer + consumer + flush service**: `CoalesceKey` (record struct `(TenantId, Sku, ChannelType)`); `CoalescingBuffer` (singleton `ConcurrentDictionary` with `AddOrUpdate` last-by-`ObservedAt` tiebreaker — protects against MT redelivery / out-of-order arrival regressing published stock; `SnapshotAndClear` snapshot-then-TryRemove handles concurrent writes mid-drain); `StockLevelChangedConsumer` (fans out per (tenant, sku) to all active channels via `IChannelLookupPort`, stamps `IsFlashSale` via `ISkuFlagRepository`); `CoalesceFlushService` (`BackgroundService` with `PeriodicTimer(CoalesceWindowMs)`); `PushIntent` record + canonical `BuildIdempotencyKey` static helper; `IPerTenantQueue` port; `StockSyncOptions { CoalesceWindowMs=500, ActiveChannels=["shopee"] }`. 19 unit tests (7 buffer + 5 consumer + 7 flush).
- **U4 Per-tenant priority queue + token bucket registry**: `PerTenantQueue` (per-tenant pair of bounded `Channel<PushIntent>` lanes via `ConcurrentDictionary<Guid, TenantQueuePair>` lazy alloc; `BoundedChannelFullMode.DropOldest`; `ReadNextAsync` strict-priority loop: TryRead-high → TryRead-normal → `Task.WhenAny(WaitToReadAsync ×2)` → retry from top); `TenantChannelBucketRegistry` (`IDisposable` registry of `TokenBucketRateLimiter` per (tenant, channel) via built-in `System.Threading.RateLimiting`; `AcquireAsync` returns `ValueTask<RateLimitLease>` for caller-controlled dispose; QueueLimit overflow surfaces as `IsAcquired=false`, not exception); `StockSyncOptions.TokenBucket { Sustain=10, Burst=50, QueueLimit=100 }` + `QueueCapacity { HighCap=1000, NormalCap=10000 }`. 11 unit tests (5 queue + 6 bucket).
- **U5 Breaker registry + push pipeline + dispatcher + push log repo**: `PushPipelineFactory` (builds `ResiliencePipeline<Result>` per (tenant, channel) with `CircuitBreakerStrategyOptions<Result>` + `CircuitBreakerStateProvider`; `ShouldHandle = PredicateBuilder<Result>().Handle<Exception>().HandleResult(r => !r.IsSuccess)`; logs Warning on Open, Info on Closed/HalfOpen); `TenantChannelBreakerRegistry` (lazy `GetOrAdd<(Guid, string), PushPipelineBundle>` + `GetState(...)` diagnostics — Closed/Open/HalfOpen/Isolated); `PerTenantDispatcherService` (`BackgroundService` enumerates Ready tenants once on startup via `ITenantCatalog.GetReadyTenantsAsync`; per-tenant `Task.Run` loop: queue.ReadNext → up-front breaker GetState (MarkBreakerOpen + continue if Open) → bucket.AcquireAsync (drop + Debug log on overflow) → pipeline.ExecuteAsync(adapter.PushStockUpdate) → MarkSucceeded/Failed log row; catches `BrokenCircuitException` mid-execute, distinguished from up-front gate in audit); `IPushLogRepository` + `PushLogRepository` (scoped, UNIQUE-23505 idempotent catch); `StockSyncOptions.Breaker { MinimumThroughput=5, BreakDurationSeconds=60, SamplingDurationSeconds=30 }`. 10 unit tests + 4 integration tests against Testcontainers Postgres.
- **U6 ShopeeAdapter.PushStockUpdate body + mock /update_stock**: replaces Sprint-4 deferred stub. `HttpRequestMessage` rebuilt per retry attempt (single-shot send mirrors Sprint-3-redux `MockShippingProvider`); `X-ShopFlow-Idempotency-Key` header (internal audit, not Shopee API contract — Phase-3 either drops or translates); status mapping (`shopee.push.rate_limited` / `5xx` / `4xx` / `transport`). `ShopeeStockUpdatePayload` records with snake_case `[JsonPropertyName]` + dedicated internal `ShopeeJson.Options` (kept separate from camelCase `OutboxJsonOptions.Default` to prevent Sprint-2.5-class silent drift). Shopee mock `POST /api/v2/product/update_stock` endpoint + `ChaosState.IsStockUpdateChaosActive` flag (process-wide; per-tenant chaos is Phase-3); namespaced `MockEntryPoint` marker class for `WebApplicationFactory<T>`. 9 adapter unit tests + 2 mock round-trip integration tests.
- **U7 SkuFlag repository + caching wrapper + admin endpoint**: `SkuFlagRepository` (scoped EF inner; UNIQUE-23505 catch + `ChangeTracker.Clear` + reload + idempotent `SkuFlag.SetFlashSale`); `CachingSkuFlagRepository` (singleton wrapper; 5-min TTL `ConcurrentDictionary<(tenantId, sku), CacheSlot>`; crude LRU eviction at 10k entries; opens DI scope + binds `RequestContext` via `ITenantCatalog.LookupByIdAsync` + K12 pattern before delegating to scoped inner); `SkuFlagsController` `PUT /api/skus/{sku}/flag` body `{ isFlashSale: bool }` → 204. **KTD7 (emerged mid-sprint)**: `ISkuFlagRepository` port signature changed to take explicit `Guid tenantId` — consumer (U3) + dispatcher (U5) call from singleton context without ambient `RequestContext`; cache key + scope binding both need explicit tenant. Updated consumer call site + 4 NSubstitute call sites in U3 unit tests. 8 cache unit tests + 7 integration tests + 1 Skip'd controller placeholder (Program.cs full composition lands in U8).
- **U8 Full Api composition + Aspire + Gateway + diagnostics**: `AddStockSyncModule` (`IDbContextFactory<StockSyncDbContext>` via K12 + scoped bridge; scoped: `SkuFlagRepository`, `IPushLogRepository`; singleton: `ISkuFlagRepository → CachingSkuFlagRepository`, `IChannelLookupPort`, `ICoalescingBuffer`, `IPerTenantQueue`, `TenantChannelBucketRegistry`, `PushPipelineFactory`, `TenantChannelBreakerRegistry`, `TimeProvider.System`; hosted: `CoalesceFlushService`, `PerTenantDispatcherService`, `MultiplexedOutboxDispatcher<StockSyncDbContext>`; `AddOutboxRoute<StockLevelChangedV1>(SendKind.Publish)`); `ChannelLookupPort` singleton reading `StockSyncOptions.ActiveChannels`; full `Program.cs` (replaces U1 stub; scans `StockSyncDbContext.Assembly` + `StockLevelChangedConsumer.Assembly` so MT discovers the consumer); `SyncStateController` `GET /api/sync/state` with class-level `[SkipTenantRouting]` guarded by `StockSync:DiagnosticsEnabled` flag (default false; Development override sets true); Aspire `AddProject<Projects.ShopFlow_StockSync_Api>("stocksync-api")` with postgres + rabbitmq deps; Gateway routes `/api/sync/{**catch-all}` + `/api/skus/{**catch-all}` → `stocksync-cluster` (`http://stocksync-api:8080`). 3 composition integration tests (DI verification + diagnostics 404/200 toggle).
- **U9 Happy-path integration + scale-gate Skip'd slots**: `StockSyncHappyPathTests` (Category=Integration, single-tenant end-to-end via `WebApplicationFactory<Program>` + `MessageBus:Transport=InMemory` + `FakeChannelAdapterFactory` recorder; provisions catalog + tenant DBs on Testcontainers Postgres; drives `StockLevelChangedV1` via `IPublishEndpoint.Publish`; asserts factory recorded the push with expected SKU + qty + `stock_sync_push_log` has Success row); `Drivers/FakeChannelAdapterFactory` + `Drivers/TenantBurstDriver` (parallel-batch 2k/s direct outbox insert) + `FairnessCalculator` helpers; `MultiTenantStockSyncScaleGateTests` ships 2 Skip'd `Category=Load` slots (R8 noisy-neighbor 5 tenants × A burst 2k/s × 5min × B-E p99 < 30s × fairness ≥ 0.85; R9 breaker recovery chaos toggle). **Sprint-4 U9 precedent** — production primitives all proven by U3-U8 unit + integration coverage; wall-time measurement deferred to Sprint-5.5 follow-up (multi-tenant Aspire boot + real Shopee mock alongside StockSync.Api).
- **U10 sign-off**: this CHANGELOG entry + sign-off doc at `docs/phase-gates/2026-05-17-sprint-5-signoff.md` + README/CLAUDE.md "Current stage" updates + plan frontmatter `status: completed` + annotated tag `v0.7.0-sprint-5`.
- Tests: **359 non-load unit tests** (+71 from Sprint-5: 14 U1 + 19 U3 + 11 U4 + 10 U5 + 9 U6 + 8 U7). +24 `Category=Integration` (6 U2 + 4 U5 + 7 U7 + 1 controller Skip + 3 U8 + 2 U6 + 1 U9). +2 `Category=Load` (U9 Skip'd).

**Documented limitations / carried-forward deferrals**:
- **Scale-gate wall-time measurement deferred** to Sprint-5.5 (same posture as Sprint-4 U9 → Sprint-4.5 U5). Production primitives proven by U3-U8 unit + integration coverage; the gate composes them into a multi-tenant wall-clock measurement.
- **Mock chaos state is process-wide** (not per-tenant). Sprint-4 U9 → Sprint-4.5 same precedent. Per-tenant chaos is Phase-3.
- **`StockUpdateRequest.ChannelId = Guid.Empty`** — Sprint-5 routes by channel TYPE only via `IChannelAdapterFactory.ResolveFor`. Per-tenant `ChannelId` lookup is Phase-3 work tied to Channel module's `channels` table query.
- **`PerTenantDispatcherService` enumerates tenants once on startup** — tenant-added events for runtime re-enumeration are Phase-3.
- **`CachingSkuFlagRepository` LRU eviction is crude** (one arbitrary key dropped at overflow). Phase-3 upgrade for proper touch-on-read LRU.
- **Cross-process cache invalidation** for `CachingSkuFlagRepository` is single-process-correct only — under W6 split, write on instance A → stale on instance B for 5 min. Phase-3 Redis pub/sub closes the gap.
- **Runtime smoke deferred** — Docker daemon not running on this dev machine (same Sprint-1/3/4/4.5 posture). CI runs the full integration suite; `Category=Load` is nightly.

**Carry-forward rules**:
- KTD1 set the canon shape — any future cross-module event from Inventory layers on top of `StockLevelChangedV1`'s pattern (one row per affected SKU per commit, post-commit value, follow-up SELECT for CTE paths).
- `ISkuFlagRepository`-style explicit-tenantId port signature is the canon for any future read-port consumed from singleton context (consumers, hosted services). Don't rely on ambient `IRequestContext` from singleton callers.
- The dispatcher's queue → bucket → breaker → pipeline → log pattern is the StockSync canon for any future push path. Phase-3 Lazada/TikTok adapters drop into the same `IChannelAdapter` slot the dispatcher already routes through.

**Next**: cut a fresh branch from `v0.7.0-sprint-5` and start either **Sprint-5.5** (close U9 scale-gate harness gap; multi-tenant Aspire boot + real Shopee mock alongside StockSync.Api), **Sprint-6** (Analytics module — W9-W10, read-side projections consuming the existing outbox stream including `StockLevelChangedV1`), or **Phase-3 polish** (Gateway hardening, observability dashboards, portfolio README + demo video, deployment docs).

---

## 2026-05-18 — Phase-3 Methodology Writeup complete

**Tag**: `v0.8.0-methodology-writeup`. Ships [docs/methodology.md](methodology.md) — comprehensive 7-sprint chronological case study + synthesis pattern catalog + friction section documenting the AI-assisted development methodology used to build ShopFlow WMS. Branch `feat/phase-3-methodology-writeup` cut from `v0.7.0-sprint-5`. Sign-off: [docs/phase-gates/2026-05-18-methodology-writeup-signoff.md](phase-gates/2026-05-18-methodology-writeup-signoff.md).

**Shipped** (7 implementation units U1-U7 per [plan](plans/2026-05-18-001-feat-methodology-writeup-plan.md)):

- **U1 Doc skeleton + lead-in + TOC + reference inventory**: top-level heading; hand-rolled TOC with anchors; 4-paragraph lead-in (project framing + methodology framing + honest "one project, one solo dev, one stack" disclaimer + reader expectation "no productivity multiplier claims"); section header placeholders for 7 chronological sprint subsections + 5 synthesis pattern subsections + friction + forward-looking + appendix; appendix reference inventory listing all 6 brainstorms + 9 plans + 8 sign-offs + 3 ADRs + 5 institutional learnings + 10 git tags. ~1300 words.
- **U2 Phase-0-redux + Sprint-1-redux sections**: foundation pivot (v2.0 RLS-shared → DB-per-tenant ADR-0003) + reservation ledger with conditional-CTE INSERT at READ COMMITTED. KTDs (D1-D4 + READ COMMITTED correction) documented. Honest friction: 23-file CSharpier drift, property tests pivoted twice (port shape changed), Property 5 read-back gap. ~990 words.
- **U3 Sprint-2-redux + Sprint-2.5 + Sprint-3-redux sections**: Inbound module + cross-module InboundConfirmedV1 + bin-aware AdjustAtBinAsync + RabbitMQ flip (W6→W4) → Sprint-2.5 closure (~half-day point-release for U9 outbox table collision, sets canonical deferral pattern) → Outbound saga + K12 per-tenant DbContext binding + 9 cross-module contracts + K11 multi-row CTE concurrency fix (caught by inherited Sprint-1-redux test) + U8 scale-gate-bypasses-saga honest documentation. ~1160 words.
- **U4 Sprint-4 + Sprint-4.5 sections**: Channel webhook ingress + K13 OutboxRouteRegistry close + 4 deferrals in sign-off → Sprint-4.5 closure (~1 week, R6 reversal documented: brainstorm InternalSku=null structurally impossible; canon mandates fail-whole-import). U1 field-name correction (idealized vs real Shopee fixture). Lesson: plan-time read-actual-contract checkpoint becomes Sprint-5 KTD1 antecedent. ~857 words.
- **U5 Sprint-5 section** (most-detailed): StockSync module (7th logical) + 4-layer isolation pipeline + KTD1 (replace literal R1 3-event consume with canonical StockLevelChangedV1, plan-time KTD) + KTD7 (ISkuFlagRepository port signature change mid-sprint U7, expensive — 4 NSubstitute call-site updates) + 2 Skip'd Category=Load slots per Sprint-4 U9 precedent + subagent dispatch (7/10 units, ~30% re-investigation overhead) + subagent re-dispatch friction (Sprint-5 U8 usage limit hit) + visual companion HTML mid-plan (unstuck dialogue when prose got dense) + .NET 9 SDK gap on dev machine. ~860 words.
- **U6 Synthesis pattern catalog**: 5 cross-sprint patterns extracted — Cadence (brainstorm → plan → work → sign-off; Mermaid flowchart; honest cost 10-15% sprint time), KTD discovery (plan-time vs mid-sprint emergence; Mermaid flowchart; K11/R6/KTD1 vs SharedKernel cycle/MT DSL/KTD7), Subagent dispatch (Sprint-5 evidence; ~30% re-investigation; voice drift + re-dispatch + review burden honest costs), Deferral pattern (Sprint-2.5/4.5/5.5; legit vs anti-pattern case-by-case; ~10-15% total sprint time), Context management (AGENTS.md + CLAUDE.md + session-resume hooks; growth limitation honest; visual HTML as emergency tool). Closing R11 framing: one project, one solo dev, one stack, ~10 days — evidence not prescription. ~2056 words.
- **U7 Friction + forward-looking + outro + wrap-up**: 7 named friction modes with Pattern/Cost/Mitigation framing (context window pressure mid-sprint, subagent re-dispatch on usage limit, .NET 9 SDK gap on dev machine, Skip'd deferral pattern kicking-the-can analysis, mid-sprint KTD emergence, CRLF/LF noise, doc inventory growth). Forward-looking section: 4 process improvements (`.gitattributes` at project init, plan-time port-shape checklist, granular subagent checkpoint commits, visual companion HTML as standard pattern) + 4 open questions (team scaling, sprint-count scaling, deferral-as-anti-pattern, agent-shape coupling) + items deferred (public blog derivative, process improvements, reusable template repo). Plus README/CLAUDE/CHANGELOG updates + sign-off doc + tag.

**Final word count**: ~8500 words (target range 7000-9000). Reading time ~30-45 minutes end-to-end; ~15 minutes for skim via TOC.

**Documented limitations / carried-forward deferrals**:

- **Public blog post derivative** (~3000-4000 words adapted for external reader) — separate brainstorm + plan + work cycle. Deferred to follow-up sprint.
- **Process improvements based on findings** — reflection sprint that revises AGENTS.md / skill cadence based on what writing the methodology doc surfaced. Particularly: KTD7-style mid-sprint emergence — should plan-time have explicit checkpoints for "scope/lifetime/tenant-context" decisions?
- **Reusable template repo** — only if external blog feedback shows demand. Not committed.

**Carry-forward rules**:
- This doc is one snapshot. Future-self updates when new patterns surface or old patterns turn out wrong.
- The honest-framing pattern (Pattern / Cost / Mitigation) is the canonical shape for any future friction documentation in this project.
- The "one project, one solo dev, one stack" disclaimer is canon: no universal-applicability claims in derived artifacts (blog post, template repo) without re-validation evidence.

**Next**: cut a fresh branch from `v0.8.0-methodology-writeup` and start either **Sprint-5.5** (close scale-gate harness; see methodology friction mode 4), **Sprint-6** (Analytics module W9-W10), **public blog derivative**, or **process improvements sprint** (`.gitattributes` config, plan-time port-shape checklist, granular checkpoint commits — see methodology forward-looking section).

---

## 2026-05-19 — Sprint-6 Frontend Vertical Slice complete

**Tag**: `v0.9.0-frontend-vertical-slice`. Closes [Sprint-6 plan](plans/2026-05-18-002-feat-sprint-6-frontend-vertical-slice-plan.md) U1-U14 on branch `feat/sprint-6-frontend-vertical-slice` (cut from `v0.8.0-methodology-writeup`). Sign-off: [docs/phase-gates/2026-05-19-sprint-6-signoff.md](phase-gates/2026-05-19-sprint-6-signoff.md).

**Shipped**:

- **First frontend surface**. New top-level `web/` subdirectory holds a Vite 5 + React 19 + TypeScript strict + TanStack Router + TanStack Query + Zustand application. Inventory screen × Owner role end-to-end through real `Inventory.Api` WRITE controllers; 8 other screens ship as `ComingSoon` placeholders behind the auth guard so the navigation shell is complete on day one.
- **Stub-grade Auth module**. New 4-csproj quartet `ShopFlow.Auth.{Domain,Application,Infrastructure,Api}` ships a dev-mode `POST /auth/login` returning a baked JWT (`tenant_slug=yensaokhanhhoa`, `role=tenant_seller`) signed via `Auth:DevSecret`. JwtBearer scheme wired in `Inventory.Api`. Gateway route `/auth/**` registered. Sprint-7 replaces with real password hashing + refresh tokens + MFA.
- **Inventory HTTP surface**. `Inventory.Api` gains 3 controllers (`SkusController`, `InventoryController`, `AdjustmentsController`) plus MediatR queries (list SKUs, ledger, summary) + commands (adjust, create-SKU, set-threshold, set-flash-sale). `Idempotency-Key` header logged in audit table; no server-side dedup (natural dedupe via `stock_adjustments`).
- **Design canon ported**. `tokens.css` warm-neutral palette + amber-ochre accent + IBM Plex Sans/Mono self-hosted via `@fontsource`. STYLING_SPECS §3.3 delta inlined + §6.1 a11y fixes (`--ink-3` re-pointed to `--neutral-500` for AA contrast).
- **Auth + http baseline**. `useAuth` Zustand store + JWT-in-localStorage persistence + `httpClient` auto-injects `Authorization: Bearer` + `X-Tenant-Slug` + `Idempotency-Key` (per-mutation ULID) headers; 401 → logout + redirect. Login form validates email + password non-empty; dev-mode any-non-empty pair succeeds.
- **Inventory screen primitives**. `<KpiStrip>` + `<FilterStrip>` + `<SkuTable>` (driven by 2s-polling `useInventoryQuery`); `<Drawer>` primitive with focus trap + Esc handler + 150 ms slide-in; `<AllocationBar>` per-channel stacked bar (empty placeholder per trade-off #3); `<LedgerRow>` + `<LedgerDrawer>` (no-poll, invalidated by mutations).
- **Owner write paths**. `<Modal>` primitive (centered, focus trap, capture-phase Esc to coexist with Drawer); `<Toast>` + `useToast` Zustand global queue (4s success / 8s error dwell, error toasts surface idempotency-key + trace-id); `useInventoryMutations` test-first (ULID-per-call, query invalidation, error toast with key + trace-id); `<AdjustStockModal>` (delta + reason + 240-char note); `<ThresholdInlineEdit>` (optimistic UI via React 19 set-state-during-render pattern); `<Toggle>` primitive + `<FlashSaleToggle>` (optimistic + disabled-while-pending); `<CreateSkuModal>` (sku regex + 409 → inline duplicate error).
- **CI + a11y harness**. New `frontend` job in `.github/workflows/ci.yml` runs pnpm typecheck → lint → vitest (incl. a11y smoke) → build in parallel with .NET jobs. `web/src/a11y.smoke.test.tsx` runs axe-core against 6 surfaces (LoginScreen + SkuTable + AdjustStockModal + CreateSkuModal + FlashSaleToggle + ToastViewport). vitest-axe@0.1.0 type augmentation shim added (`web/src/types/vitest-axe.d.ts`) for Vitest 2.x compatibility. **SkuTable `nested-interactive` violation caught + fixed by the new harness** (row dropped button semantics; SKU cell hosts the button).
- **Test counts**: 221 frontend Vitest tests across 27 test files (Sprint-6 baseline; was 0); +2 backend Auth.UnitTests = 361 total backend unit. 6 axe assertions in the a11y smoke suite. Bundle size: vendors `index.js` 311.16 kB (gz 97.93 kB); inventory chunk 42.15 kB (gz 13.44 kB).
- **Vercel agent-skills applied**. `.claude/skills/` ships 7 skills from `vercel-labs/agent-skills`; U11-U13 audited code against `vercel-react-best-practices` (`rerender-derived-state-no-effect` applied to ThresholdInlineEdit + FlashSaleToggle), `vercel-composition-patterns` (children + footer slot composition, no boolean-prop proliferation), and `web-design-guidelines` / `vercel-labs/web-interface-guidelines` (autoComplete, inputMode, spellCheck, aria-live, aria-label, htmlFor, role=alert; jsx-a11y/no-autofocus respected). Caught the SkuTable nested-interactive regression in U13.

**Documented limitations / carried-forward deferrals** (full list in [sign-off](phase-gates/2026-05-19-sprint-6-signoff.md) "Sprint-6 trade-offs locked in" + "Carried-forward deferrals" sections):

- No new schema migration this sprint (cosmetic SKU columns wait for Sprint-7); threshold + flash-sale held in `InMemorySkuMetadataStore` singleton.
- Fake auth (Sprint-7 ships real); 2-s polling (Sprint-7 swaps in SignalR push); flash-sale single-endpoint (Sprint-7 also writes StockSync `/flag`).
- vitest-axe@0.1.0 type shim is a workaround pending upstream PR or fork migration.
- Inbound module has no frontend UI yet (only Inventory ships in this slice).
- Sprint-5.5 scale-gate harness still deferred (Sprint-4 U9 → Sprint-4.5 precedent).

**Next**: cut a fresh branch from `v0.9.0-frontend-vertical-slice` and start either **Sprint-7** (real auth + SignalR push + cosmetic SKU schema expansion), **Sprint-7 alt** (Orders screen or Dashboard — second vertical slice), **Sprint-5.5** (backend-only scale-gate closure, runs parallel to Sprint-7), or the **public blog derivative** / **process improvements** items still carried from the methodology writeup.

---
