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
