# ADR-0002: Bootstrap as a modular monolith; mechanical 6-service split planned for W6

- **Status**: Accepted
- **Date**: 2026-04-27
- **Deciders**: solo dev (longuit2002-blip)
- **Supersedes**: —
- **Superseded by**: —

---

## Context

`02-technical-design-document.md.docx` §6 names the central tension of this build:

> "A reviewer is right to ask: does a 12-week portfolio project earn six microservices? The answer is that it doesn't, and we know it. The architectural value of the split is that it forces clean boundaries — you cannot take a shortcut and join two aggregates in a LINQ query. The operational cost is real: six containers, six deploys, network hops, distributed tracing just to debug a simple flow. At 1 developer and 1 tenant, the cost exceeds the value."

Standing up six service hosts on day one means six near-identical Program.cs files, six Dockerfile entries, six health endpoints, six OTel registrations, six Testcontainers fixtures, six per-service migrations — all before any domain code ships. The risk register (`01-product-development-plan.md.docx` §10) names "Distributed systems debugging eats sprint time (Medium impact, High likelihood)" as a top concern; multi-process orchestration in Phase-0 is the most predictable way to realize it.

The design's own §6.1 acknowledges this. The original choice was to ship microservices anyway "because the portfolio story is about distributed-system design, and we are honest in the README about the tradeoff." That is a legitimate stance — but it is not the only legitimate stance. The ideation pass surfaced an alternative: stand up the boundaries (separate `.csproj` per bounded context, all the modular-monolith discipline) without paying the cross-process tax until the architecture genuinely demands it.

The trigger that genuinely demands cross-process messaging is the **channel adapter framework** (`02-technical-design-document.md.docx` §8) arriving in Phase-2 Sprint-4 (W6). That subsystem is fundamentally async, fan-out-shaped, and rate-limited per external endpoint. It is the first place where in-process MediatR stops being a tenable abstraction and where MassTransit-over-RabbitMQ earns its operational complexity.

## Decision

**Phase 0 and Phase 1 (W1-W5) ship one .NET solution with six logical modules in separate `.csproj` quartets, running as a single deployable host.** Modules communicate via in-process MediatR and an in-memory MassTransit transport. All cross-cutting discipline (`tenant_id` on every row, RLS policies, outbox interceptor, correlation-context propagation, Result<T> error handling, Roslyn-analyzer-enforced canon) is wired from the first commit — the modular monolith is not a "we'll add the rigor later" stance.

**Week 6 is a planned mechanical-split event.** The split is its own implementation plan with its own scale gate. The split:

1. Extracts each module's `*.Api` project into its own deployable host (one process per module).
2. Flips MassTransit transport configuration from in-memory to RabbitMQ (the `services.AddShopFlowDefaults(...)` call already registers MassTransit; only the transport binding changes).
3. Introduces the cross-process correctness regression gate as the validation criterion: the same property suite that passes in-process must continue to pass after the split, plus a new latency-bound assertion for the cross-process hop.

The split is *mechanical* by design — no architectural rewrite. Modules already have separate `.csproj` files; their interfaces are already published in `ShopFlow.Contracts`; their data ownership is already isolated by EF DbContext-per-module. The only material change is the deployment topology and the bus transport.

## Rationale

- **Engineering judgment is more valuable than architectural ceremony.** A reviewer evaluating senior-engineer judgment will give more weight to "we deferred microservices until the operational complexity bought us something" with a written ADR + W6 gate than to "we paid the multi-process tax from day one because microservices are the goal." This is the more defensible portfolio narrative — not the less.
- **The W6 trigger is real.** The channel adapter framework's coalescing buffer + per-channel rate limiting + priority queue (`02-technical-design-document.md.docx` §8.2) is the first subsystem where the modules genuinely *need* to be in different processes — Channel must isolate fault from Inventory, must rate-limit independently per marketplace, and must scale horizontally without affecting Inventory's reservation hot path. The first 5 weeks have no such requirement; in-process MediatR is a faithful representation of the behavior.
- **The discipline that matters is the boundary, not the topology.** `02-technical-design-document.md.docx` §6.2: "the most common failure of a microservices architecture is internal sprawl — a 'service' that's a ball of mud on the inside is worse than a monolith." The modular monolith with .csproj-enforced layering and a Roslyn-analyzer-enforced canon (ADR-0006 to come; see `docs/ideation/2026-04-27-shopflow-wms-bootstrap-ideation.md` idea #6) catches the internal-sprawl failure mode at compile time. The cross-process split adds nothing to that protection.
- **Solo-dev velocity is the binding constraint.** Two and a half weeks of W1-W2 are spent on Phase 0; if six service hosts consume 30% of that budget on duplicated wiring, U6 (Inventory blessed reference) and U8 (test-first harnesses) — the units that matter — get short-changed. One host preserves the budget for the work the design actually depends on.
- **The risk register's top "High likelihood" entry is mitigated.** Plan §10 names "Distributed systems debugging eats sprint time" as Medium/High. In-process modular monolith eliminates this risk for the first 5 weeks. When it returns at W6, the harnesses (U8) and the observability stack (U9) are already in place to handle it — versus paying the cost throughout Phase 0 with neither.

## Consequences

### Positive

- Phase 0 ships one container, one Program.cs, one set of health endpoints. The six near-identical replications still happen (U10) but each is `.csproj`-shaped, not host-shaped — order of magnitude cheaper.
- Domain code in W3-W5 (Phase-1 Sprints) lands without cross-process serialization debugging. Sagas (Phase-1 Sprint-3) run in-process, so failure modes are stack traces, not correlation-ID excavation.
- The README opens with the eventual 6-service architecture diagram and labels "Phase 0-1 = modular monolith stage." Reviewers see the topology and the engineering judgment that delayed it.
- The W6 split is a *demonstrable* engineering event: there is a before-state (in-process), an after-state (multi-process), and a gate (correctness regression test) that proves nothing was lost. This is more compelling than "we shipped multi-process and asked you to trust it works."

### Negative

- **The split is itself non-trivial work in W6.** A future plan covers it; the budget is roughly 2-3 days of W6. If that budget overruns, Phase 2 sprints slip. Mitigation: the split is *mechanical* by construction (boundaries already drawn, contracts already published) — overrun risk is bounded.
- **Reviewers who skim for "microservices" pattern-matching may bounce before reaching the W6 split.** Mitigation: README leads with the final 6-service diagram and the modular-monolith stage label; ADR log is immediately discoverable in `docs/adr/`.
- **MassTransit's in-memory transport is a less-realistic substrate than RabbitMQ.** Specifically, in-memory transport does not exercise wire-format serialization or broker-side retry semantics. Mitigation: integration tests that touch the bus use Testcontainers RabbitMQ (per `02-technical-design-document.md.docx` §19.2); the in-memory transport is for unit-level handler tests only.
- **Some failure modes only surface after the split** (RabbitMQ partition handling, broker-down recovery). These are out of scope for Phase 0-1 anyway — they belong to Phase-2 Sprint-5 chaos testing — so the deferral is consistent with the design's existing phasing.

### Neutral

- The .csproj layout is identical to the eventual microservices layout (`02-technical-design-document.md.docx` §5). The split changes hosting topology and transport binding only.
- AGENTS.md (U2) and the cross-cutting NuGet (U5) are designed for the post-split topology from the start, so no rule changes are needed at W6.

## W6 split — gate criteria

The W6 split lands as its own plan with its own scale gate. The gate is binary:

1. **Correctness regression**: the FsCheck reservation-ledger property suite (U8) and the stock-sync load harness (U8) — both quoting the same assertions from `01-product-development-plan.md.docx` §299 and §316-323 — must produce the same pass/fail profile after the split as before, with one tolerance: cross-process latency adds ≤ 50ms p99 to any single-hop interaction. This bound must be measured, not assumed.
2. **Operational regression**: cold-start under `aspire run` (per ADR-0001) must remain < 90s after the split despite running 6 host processes instead of 1. If this fails, ADR-0002 is revisited (we may need to keep some modules co-hosted at staging time and only split in production).
3. **Saga regression**: the fulfillment saga (`02-technical-design-document.md.docx` §10) must complete the Reserve → Pick → Pack → Ship flow end-to-end with MassTransit's RabbitMQ transport, identical state transitions to the in-process version.

If any gate fails, the split is rolled back (single commit revert is sufficient since the change is mechanical) and a follow-up ADR captures the lesson and the revised plan.

## When this breaks

Revisit this ADR if any of the following hold before W6:

1. **A subsystem before W6 genuinely needs cross-process isolation.** None is currently identified; the design's only such subsystem is the channel adapter framework which sits at W6 by construction. If a Phase-1 sprint surfaces a real need (e.g., Inventory's reservation hot path needs CPU isolation from Inbound's bulk receiving), supersede this ADR with one that splits earlier.
2. **The mechanical-split assumption breaks.** If during Phase-1 we discover a cross-module shortcut that the modular-monolith discipline did not prevent (e.g., a shared DbContext, a direct entity reference across module boundaries), the split is no longer mechanical and a refactor is required first. This is the kind of failure the Roslyn analyzer (ADR-0006 to come) is designed to catch at compile time. If it slips through, supersede.
3. **Reviewer feedback on the public repo signals that the modular-monolith stage is misread as "didn't actually build microservices."** Mitigation: the README's lead paragraph, the ADR log, and the W6 split commits should make the narrative legible. If they do not, an additional `docs/architecture-narrative.md` may be added — but the underlying architecture decision stays.

If W6 arrives and the channel adapter framework is reshaped (e.g., the design pivots away from per-marketplace isolation), the trigger condition for this ADR's split commitment changes. A new ADR resets the trigger.

## References

- `02-technical-design-document.md.docx` §6 (the honest tradeoff), §8 (channel adapter framework — the W6 trigger), §10 (saga), §165 (no cloud lock-in)
- `01-product-development-plan.md.docx` §10 (risk register), §299 + §316-323 (correctness assertions to preserve through the split)
- `docs/ideation/2026-04-27-shopflow-wms-bootstrap-ideation.md` idea #1
- `docs/plans/2026-04-27-001-feat-shopflow-wms-phase-0-bootstrap-plan.md` U6, U10 (the layout that survives the split)
- ADR-0001 (Aspire AppHost orchestrates the W6 multi-host topology too — split changes resource registration, not the orchestrator)
