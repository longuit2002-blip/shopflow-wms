# AGENTS.md — ShopFlow WMS Rule Canon

This is the executable canon that AI-pair-programming agents (Claude Code, Cursor, Copilot, Codex, Aider) anchor against when writing code in this repo. It is **not** project context — see `CLAUDE.md` for that. This file is the *rules*, kept short on purpose. Hard budget: 200 instructions; current count is well below.

**How this file evolves**: every reviewer/Copilot suggestion that violates the canon is a missing rule, not a one-off correction. When that happens, add the rule here, then fix the violation. The Roslyn analyzer in `src/Shared/ShopFlow.SharedKernel/Analyzers/` enforces the rules that can be made executable; the rest are review-time signals.

**The blessed reference**: when in doubt, copy the pattern from `src/Services/Inventory/`. Until a pattern is documented in this file or in `docs/solutions/`, defer to ADR-0001, ADR-0002, ADR-0003, and `02-technical-design-document.md.docx` §1–§20 (v3.0). Note v3.0 reorganization: §1 multi-tenancy, §2 provisioning, §4 reservation ledger (was §7), §5 outbox + sync (was §11). The plain-text extracts in `docs/source/` lag the .docx by one extraction run.

**Compounding learnings**: when a fix takes >5 minutes to diagnose or is non-obvious from the project tree, capture it in `docs/solutions/` ([README](docs/solutions/README.md)) so future agents (and future-you) don't re-discover. The compound principle: institutional memory > individual genius.

---

## 1. Working stance

1. All project-related artifacts live in-tree. No machine-local memory, no per-user paths in code or config.
2. Source-of-truth specs are `01-product-development-plan.md.docx` and `02-technical-design-document.md.docx`. Plain-text extracts at `docs/source/` are derived; do not edit them.
3. Architectural decisions land as numbered ADRs in `docs/adr/`; ADRs are immutable once accepted. Reversal is a new ADR that supersedes by link.
4. Plans live in `docs/plans/`; the active one drives implementation. Do not edit plan bodies during execution — git is the progress record.
5. Repo-relative paths everywhere. No absolute paths in plans, code, configs, or commit messages.

---

## 2. Architecture and layering

6. Each bounded context (Inventory, Inbound, Outbound, Channel, Analytics) is a quartet of `.csproj`: `Domain`, `Application`, `Infrastructure`, `Api`. Analytics omits `Domain` per Tech Design §5. Gateway is a single project (YARP).
7. Dependency arrows point inward only: `Api` → `Application` + `Infrastructure`; `Infrastructure` → `Application` + `Domain`; `Application` → `Domain`; `Domain` → nothing.
8. CI fails on wrong-direction project references. Layering is enforced by `.csproj`, not by review.
9. `Domain` has zero framework references — no EF Core, no MassTransit, no ASP.NET. Pure C# + `ShopFlow.SharedKernel.Domain` only.
10. Cross-module reads never hit another module's DbContext. Communicate via `ShopFlow.Contracts` integration events through MassTransit.
11. Modules ship in one host (in-memory MediatR + in-memory MassTransit transport) through W5. W6 mechanical split flips transport binding only — see ADR-0002.
12. New cross-cutting concern? It lives in `ShopFlow.SharedKernel`, exposed via `services.AddShopFlowDefaults(...)`. Per-module composition only adds module-specific registrations on top.
13. Do not introduce a service-locator or static accessor for cross-cutting concerns. Inject via constructor.

---

## 3. Multi-tenancy and data access

> **Per ADR-0003**, tenancy is database-per-tenant on a shared Postgres cluster. The database identity is the tenant boundary; there is no `tenant_id` column on business tables, no RLS, no global query filter. The rules below replace the v2.0 RLS-shaped rules.

14. Every persistent table belongs to exactly one tenant DB or to the control plane DB (`shopflow_control`). No business table carries `tenant_id` — the database itself is the boundary.
15. Tenant routing happens **only** in middleware. `IRequestContext.TenantId` / `TenantSlug` / `DbConnectionString` are populated there from header / JWT claim / subdomain (priority order, conflicts rejected with 403). Code below middleware reads `IRequestContext` and trusts it; re-validation in handlers is forbidden by `ShopFlow0004`.
16. Every EF query goes through a tenant-scoped repository. Raw `DbSet<T>` access in `Application` or `Api` is forbidden — analyzer `ShopFlow0001` fails the build.
17. Construct DbContexts via `IDbContextFactory<TContext>` only. The factory reads `IRequestContext.DbConnectionString` per request. Never instantiate a DbContext with an explicit connection string in business code (`ShopFlow0003`). Never mutate the connection string on an existing DbContext — EF's model cache leaks across tenants.
18. Background workers carry tenant context in message headers. Consumer middleware reads the header, opens a scope, sets `IRequestContext.TenantId`, then resolves services. Never share a DbContext across messages.
19. The control-plane catalog (`shopflow_control.tenants`) is accessed via `ITenantCatalog` only. No business code reads from `shopflow_control` directly. Catalog migrations are owned by `ShopFlow.ControlPlane.Migrations`; module migrations target tenant DBs only.
20. Tenant DB lifecycle (`CREATE DATABASE`, `DROP DATABASE`) runs only via `shopflow-migrate provision|archive` — never from application code, never from EF migrations. Provisioning bypasses PgBouncer; application traffic always goes through PgBouncer.
21. Cross-tenant routing correctness is tested in CI: a request with tenant A's headers must never return tenant B's data — verified via the `CrossTenantRoutingTests` suite per Phase-0-redux U8-redux. A failure is a P0 incident.
22. Schema migrations are backward-compatible for one release: add columns/indexes before reads, expand types before shrinking, never drop in the same release that stops writing. Migrations apply per-tenant via `shopflow-migrate apply --target=<version>`; failure stops the run and reports the failed tenant for retry.
23. Hand-authored migration classes carry **both** `[Migration("<timestamp>_Name")]` and `[DbContext(typeof(<DbContext>))]` attributes. Without them `MigrateAsync()` is a silent no-op (per `docs/solutions/2026-05-10-ef-migration-needs-attributes.md`). The migration smoke test in per-PR CI guards this contract.

---

## 4. Domain and error handling

21. Domain methods return `Result<T>` for expected failures (oversold, idempotency-key-reused, validation). They throw only for programmer errors (invariant violations).
22. `Application` handlers return `Result<T>`; the API layer maps `Result.Failure` to the appropriate HTTP status via problem-details middleware.
23. Domain events are raised on aggregates via `RaiseDomainEvent` and cleared by the persistence layer after the outbox interceptor collects them.
24. Aggregate roots inherit from `AggregateRoot` (which inherits from `BaseEntity`). Value objects inherit from `ValueObject`.
25. Value objects validate at construction and throw `ArgumentException` on invalid input. They are immutable. Equality is structural.
26. Do not use exceptions for control flow. Do not use `Try`/`out` patterns when `Result<T>` fits.
27. Domain code does not log. Logging is an `Application`/`Infrastructure` concern.

---

## 5. Async, time, and concurrency

28. Every I/O method is async. Suffix is `Async`. Returns `Task` or `ValueTask`, never `void`.
29. Never `.Result` or `.Wait()` on a Task. Never `.GetAwaiter().GetResult()` outside `Main`. Analyzer-enforced where statically detectable.
30. Pass `CancellationToken` through every async call chain. Do not swallow `OperationCanceledException`.
31. `DateTime.Now` is forbidden anywhere. Use `DateTime.UtcNow` or, preferably, inject `TimeProvider` (or an `IClock` shim if not on .NET 8 timing API). Analyzer `ShopFlow0004` fails the build.
32. `DateTimeOffset.Now` is forbidden for the same reason. Use `DateTimeOffset.UtcNow`.
33. `Random.Shared` only — never `new Random()` in hot paths.
34. Concurrency primitives (locks, semaphores) are last-resort. Prefer immutable data + message passing.

---

## 6. Outbox, messaging, and idempotency

35. Domain events are persisted via the outbox (atomic with the business write) by the `OutboxInterceptor` in `ShopFlow.SharedKernel`. Modules never call `IPublishEndpoint.Publish` directly during a write transaction.
36. Webhook receivers persist the raw payload + `(channel_id, provider_event_id) UNIQUE` *before* enqueuing for processing. Duplicate deliveries return 200 without re-processing — never silently dedupe via Redis.
37. Webhook handlers carry the `[Idempotent]` attribute. Analyzer `ShopFlow0003` fails the build on missing attribute.
38. Outbound HTTP calls to channels (Shopee, Lazada, future marketplaces) carry an idempotency key. Retries reuse the key.
39. Every published integration event carries `tenant_id`, `correlation_id`, and `occurred_at` UTC in its envelope.
40. Correlation context propagates via W3C TraceContext on the message envelope. Analyzer `ShopFlow0002` fails the build on missing propagation.
41. Saga state machines persist via MassTransit (Postgres-backed at MVP, Redis-backed at scale per Tech Design §10.4). State transitions are explicit; no implicit transitions.

---

## 7. Naming conventions

42. C# files: PascalCase. One public type per file (with rare nested-type exceptions). File name matches the public type.
43. Aggregate roots: noun, no suffix (`StockItem`, `Order`). Domain events: past-tense participle (`StockReservedEvent`, `OrderShipped`).
44. Commands: imperative-mood + `Command` suffix (`ReserveStockCommand`). Queries: noun + `Query` suffix (`GetAvailabilityQuery`).
45. Handlers: corresponding command/query name + `Handler` suffix (`ReserveStockHandler`).
46. Repositories: aggregate name + `Repository`. Interface: `I` + same. Place interface in `Application/Ports/`, implementation in `Infrastructure/Repositories/`.
47. Database tables and columns: snake_case (Tech Design §7.2 verbatim). C# property names: PascalCase. EF Core's `[Column]` attribute or fluent config maps the two.
48. Migrations: `YYYYMMDDhhmmss_DescriptiveName` (EF default + UTC timestamp).
49. Tests: `Arrange/Act/Assert` structure, named `MethodUnderTest_Scenario_ExpectedOutcome`. xUnit `[Fact]` for fixed cases, `[Theory] + [InlineData]` for parameterized.
50. Async test methods end in `Async`.

---

## 8. Testing

51. Unit tests cover the `Domain` and `Application` layers without I/O. They run in < 5 seconds total per module.
52. Integration tests use Testcontainers for real Postgres + real RabbitMQ. They run via `[Collection("Integration")]` to share container lifetime where state is read-only.
53. Pin Testcontainers image tags (`postgres:16`, `rabbitmq:3-management-alpine`). Do not float to `latest`.
54. Property-based tests on the reservation ledger and allocation engine use FsCheck. Random seeds are pinned in attributes.
55. The reservation ledger and stock-sync engine are written test-first against `NotImplementedException` stubs in W1. The harness is the spec; assertions are quoted from `01-product-development-plan.md.docx` §299, §316–§323.
56. Integration tests do not mock the layers they exist to verify (DB, broker, HTTP gateway). Mock external services (Shopee/Lazada) via the mock-channel server, not via in-process mocks.
57. Every public API endpoint has an integration test that exercises the full request→DB→outbox→event chain.
58. Cross-tenant routing correctness is tested in CI per rule 21. The `CrossTenantRoutingTests` suite is mandatory and runs on every PR. Property tests, integration tests, and the W3 noisy-neighbor scale gate are tagged `Category=Integration` and run nightly + on-demand.
59. Load tests (`tests/ShopFlow.LoadTests`) run nightly, not per-PR. Property tests run per-PR.
60. New behavior arrives with new tests; changed behavior arrives with changed tests; deleted behavior arrives with deleted tests. CI fails on uncovered new public methods in `Domain`/`Application` (coverage gate, configurable per module).

---

## 9. AI-pair-programming workflow

61. Treat every reviewer comment on an AI-assisted PR as a missing rule. Add the rule to this file in the same PR (or the next), do not just fix the violation.
62. When a rule is genuinely conditional or has exceptions, document the condition. Do not vague-ify the rule.
63. The blessed reference for any pattern is `src/Services/Inventory/` once it lands (W1 U6). Per-module `AGENTS.md` files capture only the deltas, not the full rule restatement.
64. Cap this file at 200 instructions. When close, prefer consolidating duplicates over expanding. Spillover goes to `RULES.md` (companion file, opt-in).
65. The Roslyn analyzer in `src/Shared/ShopFlow.SharedKernel/Analyzers/` is the executable subset of this canon. New analyzer rules require a new ADR.

---

## 10. Commit and PR hygiene

66. Conventional commits: `<type>(<scope>): <subject>` where `type` ∈ {feat, fix, docs, refactor, test, chore, ci, build}. Subject is imperative-mood, lowercase, ≤ 72 chars.
67. Each commit closes one logical unit. If the message would be "WIP" or "partial X", do not commit yet.
68. Commits cite the closing U-ID when implementing a plan unit (e.g., "Closes U6 of docs/plans/...").
69. Never `git add -A` or `git add .`. Stage by name to avoid accidentally committing secrets, derived artifacts, or scratch files.
70. Never `--no-verify` to bypass pre-commit hooks. Fix the underlying issue.
71. Never force-push to `main`. Force-push on feature branches is allowed but discouraged; prefer `git commit --amend` only on un-pushed commits.
72. PR titles match the conventional-commit format. PR descriptions cite the closing R-IDs and U-IDs.

---

## 11. Module shape canon

Every bounded-context module follows this layout. Diverging requires a new ADR.

73. **Csproj quartet**: `ShopFlow.<Name>.Domain` + `.Application` + `.Infrastructure` + `.Api`. Analytics is the documented exception (no Domain — read-side only per Tech Design §5).
74. **Folder layout**: `src/Services/<Name>/ShopFlow.<Name>.<Layer>/`. The module's `AGENTS.md` lives at `src/Services/<Name>/AGENTS.md` (sibling to the four csproj folders).
75. **Project reference direction** (CI-enforced via `.csproj` references): `Api → Infrastructure → Application → Domain → SharedKernel`. Wrong-direction references fail the build (rule 8 already says this; here it gets a concrete shape).
76. **Composition root extension**: each module exposes `services.Add<Name>Module(IConfiguration)` from `<Name>.Infrastructure/<Name>ServiceCollectionExtensions.cs`. The `Api/Program.cs` calls `services.AddShopFlowDefaults(...)` first, then `services.Add<Name>Module(...)`. No exceptions.
77. **DbContext-per-module**: each module that persists owns one `<Name>DbContext` at `<Name>.Infrastructure/<Name>DbContext.cs`. Migrations live at `<Name>.Infrastructure/Migrations/`. Cross-module reads never touch another module's DbContext (rule 10). DbContexts are constructed via `IDbContextFactory<TContext>` per request (rule 17); modules never new up their own DbContext nor read connection strings directly.
78. **Test layout mirrors source**: `tests/ShopFlow.<Name>.UnitTests/` (always — Domain + Application coverage), `tests/ShopFlow.<Name>.IntegrationTests/` (when DB- or API-bound).
79. **Module `AGENTS.md` is delta-only**, ≤ 50 lines: lifecycle invariants specific to this module, hard "do not simplify" warnings on load-bearing primitives, deltas from root canon. Do not restate root rules.
80. **Csproj keeps only what diverges from the defaults**. Root `Directory.Build.props` carries TargetFramework, ImplicitUsings, Nullable, LangVersion, TreatWarningsAsErrors, IsPackable. `Directory.Packages.props` carries every package version (CPM enforced — `<PackageReference>` declares Include, never Version). Test projects inherit additional defaults from `tests/Directory.Build.props`. A typical Domain csproj is ~10 lines; a typical test csproj is ~15 lines.

---

## How to consume this file

- **Claude Code, Cursor, Codex CLI, Aider**: this file is auto-loaded by the AGENTS.md cross-tool standard.
- **Copilot**: link this file in the repo description; reference it from PR templates.
- **Per-service deltas**: `src/Services/<Name>/AGENTS.md` adds module-specific rules only.
