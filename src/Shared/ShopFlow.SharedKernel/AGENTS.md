# AGENTS.md — ShopFlow.SharedKernel deltas

This is the canon foundation. Every other module's AGENTS.md inherits from the root canon (`/AGENTS.md`) and adds module-specific rules.

**Rule changes here require an ADR.** Do not edit Domain/Application/Infrastructure types in this project to "make a test green" — change the test, the consumer, or write the ADR explaining why the canon shifts. The Roslyn analyzers in `../ShopFlow.SharedKernel.Analyzers/` are the executable subset of the canon; new rules require a new diagnostic ID, a corresponding test pair, and an ADR.

## Module-local rules

1. No public type in this project may take a runtime dependency on a specific module's domain. Cross-module references are forbidden by definition — this is the kernel.
2. The `OutboxMessage` row shape is fixed and matches Tech Design §11.1 verbatim. Schema migrations land in U6's initial Inventory migration (the kernel does not own a migration of its own).
3. `services.AddShopFlowDefaults(configuration)` is the single supported composition entry point. Per-module `Program.cs` calls it once and adds module-specific registrations on top — never re-implements its work.
4. The MassTransit transport binding here is **in-memory** through W5; ADR-0002 governs the W6 flip to RabbitMQ. When that flip lands, only the `UsingInMemory(...)` line in `AddShopFlowDefaults.cs` changes — the rest of the kernel is transport-agnostic by construction.
5. Pipeline behaviors run in the order: Logging → Tracing → Validation → handler. Logging brackets the whole call so trace/timing data is captured even on validation failures.
