# AGENTS.md — ShopFlow WMS Rule Canon

This is the executable canon that AI-pair-programming agents (Claude Code, Cursor, Copilot, Codex, Aider) anchor against when writing code in this repo. It is **not** project context — see `CLAUDE.md` for that. This file is the *rules*, kept short on purpose. Hard budget: 200 instructions; current count is well below.

**How this file evolves**: every reviewer/Copilot suggestion that violates the canon is a missing rule, not a one-off correction. When that happens, add the rule here, then fix the violation. The Roslyn analyzer in `src/Shared/ShopFlow.SharedKernel/Analyzers/` enforces the rules that can be made executable; the rest are review-time signals.

**The blessed reference**: when in doubt, copy the pattern from `src/Services/Inventory/` (lands at end of W1 in U6 of `docs/plans/2026-04-27-001-feat-shopflow-wms-phase-0-bootstrap-plan.md`). Until that exists, defer to ADR-0001, ADR-0002, and `02-technical-design-document.md.docx` §5–§20.

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

14. Every persistent table carries `tenant_id UUID NOT NULL` from commit one — even at MVP single-tenant.
15. Every Postgres table has an RLS policy filtering on `tenant_id`. RLS is set up in the same migration that creates the table, never as a follow-up.
16. Every EF query that hits a tenant-scoped table goes through a tenant-scoped repository. Raw `DbSet<T>` access in `Application` or `Api` is forbidden — analyzer `ShopFlow0001` fails the build.
17. The tenant context is set per-request via `IRequestContext`, populated at the API boundary, propagated into `SET LOCAL app.tenant_id = '...'` by the EF interceptor.
18. RLS isolation is tested in CI: a connection impersonating tenant A queries a tenant-B-only table and expects zero rows.
19. Cross-tenant operations (admin / reporting) are explicit and audited; they require a distinct connection string with the RLS-bypass role.
20. Schema migrations are backward-compatible for one release: add columns/indexes before reads, expand types before shrinking, never drop in the same release that stops writing.

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
58. RLS isolation is tested in CI per rule 18.
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

## How to consume this file

- **Claude Code, Cursor, Codex CLI, Aider**: this file is auto-loaded by the AGENTS.md cross-tool standard.
- **Copilot**: link this file in the repo description; reference it from PR templates.
- **Per-service deltas**: `src/Services/<Name>/AGENTS.md` adds module-specific rules only.
